using ClientAvalonia.Services;
using ClientCore;
using ClientCore.Settings;
using Rampastring.Tools;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClientAvalonia.Domain;

/// <summary>Renderer setup aligned with DXMainClient <c>DirectDrawWrapperManager</c>.</summary>
public sealed class DirectDrawWrapperManager
{
    private const string RenderersIni = "Renderers.ini";

    private readonly List<DirectDrawWrapper> _renderers;
    private readonly string _defaultRenderer;
    private DirectDrawWrapper _selectedRenderer;

    public DirectDrawWrapper SelectedRenderer => _selectedRenderer;

    public DirectDrawWrapperManager()
    {
        _renderers = LoadRenderers(out _defaultRenderer);
        ReloadSelectedRendererFromSettings();
    }

    public IReadOnlyList<DirectDrawWrapper> GetCompatibleRenderers()
    {
        OSVersion osVersion = ClientConfiguration.Instance.GetOperatingSystemVersion();
        return _renderers.Where(r => r.IsCompatibleWithOS(osVersion) && !r.Hidden).ToList();
    }

    public void ReloadSelectedRendererFromSettings()
    {
        string savedName = UserINISettings.Instance.Renderer.Value?.Trim() ?? string.Empty;
        string rendererName = RendererNameNormalizer.Normalize(savedName);
        if (string.IsNullOrEmpty(rendererName))
            rendererName = _defaultRenderer;

        DirectDrawWrapper? resolved = FindRenderer(rendererName)
            ?? FindRenderer(_defaultRenderer)
            ?? _renderers.FirstOrDefault(r => !r.Hidden);

        if (resolved == null)
            throw new ClientConfigurationException("No renderers available from Renderers.ini");

        if (!string.IsNullOrEmpty(savedName)
            && !savedName.Equals(resolved.InternalName, StringComparison.OrdinalIgnoreCase)
            && NormalizeRendererKey(savedName) != NormalizeRendererKey(resolved.InternalName))
        {
            Logger.Log($"DirectDrawWrapperManager: renderer '{savedName}' resolved to '{resolved.InternalName}'.");
        }

        _selectedRenderer = resolved;
        GameProcessLauncher.UseQres = _selectedRenderer.UseQres;
        GameProcessLauncher.SingleCoreAffinity = _selectedRenderer.SingleCoreAffinity;
    }

    /// <summary>Windowed mode from renderer INI (CnC-DDRAW) or user settings (stock renderer).</summary>
    public bool GetEffectiveWindowedMode()
    {
        DirectDrawWrapper renderer = _selectedRenderer;
        if (!renderer.UsesCustomWindowedOption())
            return UserINISettings.Instance.WindowedMode;

        string configPath = SafePath.CombineFilePath(ProgramConstants.GamePath, renderer.ConfigFileName);
        if (!File.Exists(configPath))
            return UserINISettings.Instance.WindowedMode;

        var ini = new IniFile(configPath);
        return ini.GetBooleanValue(renderer.WindowedModeSection, renderer.WindowedModeKey, false);
    }

    /// <summary>DX DisplayOptionsPanel.Save → directDrawWrapperManager.Save().</summary>
    public void ApplyRenderer(string internalName)
    {
        DirectDrawWrapper? renderer = FindRenderer(internalName);

        if (renderer != null)
            _selectedRenderer = renderer;

        ApplySelectedRenderer();
    }

    /// <summary>Apply the selected renderer and sync windowed-mode keys before game launch.</summary>
    public void ApplySelectedRenderer()
    {
        DirectDrawWrapper renderer = _selectedRenderer;

        if (!SafePath.GetFile(ProgramConstants.GamePath, renderer.ConfigFileName).Exists)
        {
            foreach (DirectDrawWrapper other in _renderers.Where(r => r != renderer))
                other.Clean();
        }

        renderer.Apply();
        SyncRendererWindowedSettings(renderer);

        GameProcessLauncher.UseQres = renderer.UseQres;
        GameProcessLauncher.SingleCoreAffinity = renderer.SingleCoreAffinity;
        UserINISettings.Instance.Renderer.Value = renderer.InternalName;
    }

    /// <summary>Re-write renderer INI windowed keys without touching ddraw.dll (fast pre-launch path).</summary>
    public void SyncWindowedSettingsOnly()
    {
        SyncRendererWindowedSettings(_selectedRenderer);
        GameProcessLauncher.UseQres = _selectedRenderer.UseQres;
        GameProcessLauncher.SingleCoreAffinity = _selectedRenderer.SingleCoreAffinity;
    }

    public void SyncWindowedSettingsFromUi(bool windowed, bool borderlessWindowed)
        => SyncWindowedSettingsFromUi(_selectedRenderer, windowed, borderlessWindowed);

    public void SyncWindowedSettingsFromUi(DirectDrawWrapper renderer, bool windowed, bool borderlessWindowed)
    {
        if (renderer.UsesCustomWindowedOption())
        {
            WriteRendererWindowedIni(renderer, windowed, borderlessWindowed);
            return;
        }

        UserINISettings.Instance.WindowedMode.Value = windowed;
        UserINISettings.Instance.BorderlessWindowedMode.Value = borderlessWindowed;
    }

    private DirectDrawWrapper? FindRenderer(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        DirectDrawWrapper? exact = _renderers.Find(r =>
            r.InternalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        string key = NormalizeRendererKey(name);
        return _renderers.Find(r => NormalizeRendererKey(r.InternalName) == key);
    }

    private static string NormalizeRendererKey(string name)
        => new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static void SyncRendererWindowedSettings(DirectDrawWrapper renderer)
    {
        if (!renderer.UsesCustomWindowedOption())
            return;

        bool windowed = UserINISettings.Instance.WindowedMode;
        bool borderless = UserINISettings.Instance.BorderlessWindowedMode;

        string configPath = SafePath.CombineFilePath(ProgramConstants.GamePath, renderer.ConfigFileName);
        if (File.Exists(configPath))
        {
            var existing = new IniFile(configPath);
            windowed = existing.GetBooleanValue(renderer.WindowedModeSection, renderer.WindowedModeKey, windowed);

            if (!string.IsNullOrEmpty(renderer.BorderlessWindowedModeKey))
            {
                borderless = existing.GetBooleanValue(
                    renderer.WindowedModeSection,
                    renderer.BorderlessWindowedModeKey,
                    borderless);

                if (renderer.IsBorderlessWindowedModeKeyReversed)
                    borderless = !borderless;
            }
        }

        WriteRendererWindowedIni(renderer, windowed, borderless);
    }

    private static void WriteRendererWindowedIni(DirectDrawWrapper renderer, bool windowed, bool borderlessWindowed)
    {
        string configPath = SafePath.CombineFilePath(ProgramConstants.GamePath, renderer.ConfigFileName);
        var rendererSettingsIni = new IniFile(configPath);

        rendererSettingsIni.SetBooleanValue(
            renderer.WindowedModeSection,
            renderer.WindowedModeKey,
            windowed);

        if (!string.IsNullOrEmpty(renderer.BorderlessWindowedModeKey))
        {
            bool borderlessIniValue = borderlessWindowed;
            if (renderer.IsBorderlessWindowedModeKeyReversed)
                borderlessIniValue = !borderlessIniValue;

            rendererSettingsIni.SetBooleanValue(
                renderer.WindowedModeSection,
                renderer.BorderlessWindowedModeKey,
                borderlessIniValue);
        }

        SyncSpawnExecutableSection(rendererSettingsIni, windowed, borderlessWindowed);
        rendererSettingsIni.WriteIniFile();
    }

    /// <summary>Syringe runs gamemd-spawn.exe; cnc-ddraw needs per-exe windowed/resolution keys.</summary>
    private static void SyncSpawnExecutableSection(IniFile rendererSettingsIni, bool windowed, bool borderlessWindowed)
    {
        string gameExecutableName = ClientConfiguration.Instance.GetGameExecutableName();
        if (string.IsNullOrWhiteSpace(gameExecutableName))
            return;

        string spawnSection = Path.GetFileNameWithoutExtension(gameExecutableName) + "-spawn";
        rendererSettingsIni.SetBooleanValue(spawnSection, "windowed", windowed);
        rendererSettingsIni.SetBooleanValue(spawnSection, "border", !borderlessWindowed);
        rendererSettingsIni.SetBooleanValue(spawnSection, "noactivateapp", true);
        rendererSettingsIni.SetBooleanValue(spawnSection, "handlemouse", false);

        int width = UserINISettings.Instance.IngameScreenWidth;
        int height = UserINISettings.Instance.IngameScreenHeight;
        if (width > 0 && height > 0)
        {
            rendererSettingsIni.SetIntValue(spawnSection, "width", width);
            rendererSettingsIni.SetIntValue(spawnSection, "height", height);
        }
    }

    private static List<DirectDrawWrapper> LoadRenderers(out string defaultRenderer)
    {
        var renderers = new List<DirectDrawWrapper>();
        var renderersIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), RenderersIni));

        IReadOnlyList<string>? keys = renderersIni.GetSectionKeys("Renderers");
        if (keys == null)
            throw new ClientConfigurationException("[Renderers] not found from Renderers.ini!");

        foreach (string key in keys)
        {
            string internalName = renderersIni.GetStringValue("Renderers", key, string.Empty);
            renderers.Add(new DirectDrawWrapper(internalName, renderersIni));
        }

        OSVersion osVersion = ClientConfiguration.Instance.GetOperatingSystemVersion();
        defaultRenderer = renderersIni.GetStringValue("DefaultRenderer", osVersion.ToString(), string.Empty);

        if (string.IsNullOrEmpty(defaultRenderer))
            throw new ClientConfigurationException("Invalid or missing default renderer for operating system: " + osVersion);

        return renderers;
    }
}
