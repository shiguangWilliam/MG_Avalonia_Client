using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ClientAvalonia.Core;
using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientCore;
using ClientCore.Enums;
using ClientCore.Settings;
using Rampastring.Tools;
using System.Linq;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>
/// Load/save display tab options (DX <c>DisplayOptionsPanel</c> Load/Save).
/// Renderer + windowed mode are handled here instead of generic INI binding.
/// </summary>
public static class DisplayOptionsApplier
{
    public static void Apply(UiNodeViewModel? optionsRoot)
    {
        if (optionsRoot == null)
            return;

        try
        {
            ClientCoreBootstrap.TryEnsureInitialized(null, out _);
            GameRendererBootstrap.Manager.ReloadSelectedRendererFromSettings();
            LoadRendererDropdown(optionsRoot);
            LoadIngameResolution(optionsRoot);
            LoadClientResolution(optionsRoot);
            LoadBackBuffer(optionsRoot);
            LoadVisualStyleDropdown(optionsRoot);
            SyncWindowedControlsFromRenderer(optionsRoot, GameRendererBootstrap.Manager.SelectedRenderer);
        }
        catch (Exception ex)
        {
            Logger.Log("DisplayOptionsApplier.Apply failed: " + ex);
        }
    }

    /// <summary>Persist display options and deploy ddraw files (DX options save + Apply).</summary>
    public static void Save(UiNodeViewModel optionsRoot)
    {
        try
        {
            DirectDrawWrapperManager manager = GameRendererBootstrap.Manager;
            DirectDrawWrapper targetRenderer = ResolveSelectedRenderer(optionsRoot, manager);
            bool windowed = ReadCheck(optionsRoot, "chkWindowedMode");
            bool borderlessWindowed = ReadCheck(optionsRoot, "chkBorderlessWindowedMode");

            SaveIngameResolution(optionsRoot);
            SaveClientResolution(optionsRoot);
            SaveBackBuffer(optionsRoot);

            if (targetRenderer.UsesCustomWindowedOption())
            {
                manager.SyncWindowedSettingsFromUi(targetRenderer, windowed, borderlessWindowed);
                UserINISettings.Instance.WindowedMode.Value = false;
            }
            else
            {
                UserINISettings.Instance.WindowedMode.Value = windowed;
                UserINISettings.Instance.BorderlessWindowedMode.Value = borderlessWindowed
                    && string.IsNullOrEmpty(targetRenderer.BorderlessWindowedModeKey);
            }

            string internalName = targetRenderer.InternalName;
            UserINISettings.Instance.Renderer.Value = internalName;
            manager.ApplyRenderer(internalName);
            UserINISettings.Instance.SaveSettings();

            Logger.Log($"DisplayOptionsApplier: saved renderer={internalName}, windowed={windowed}, borderless={borderlessWindowed}, UseQres={GameProcessLauncher.UseQres}.");
        }
        catch (Exception ex)
        {
            // Renderer deployment must never crash the client; report and keep the session alive.
            Logger.Log("DisplayOptionsApplier.Save failed: " + ex);
            LastSaveError = ex.Message;
        }
    }

    /// <summary>Last renderer-related failure shown in the status bar (null = clean save).</summary>
    public static string? LastSaveError { get; private set; }

    private static void LoadRendererDropdown(UiNodeViewModel optionsRoot)
    {
        UiNodeViewModel? ddRenderer = FindVm(optionsRoot, "ddRenderer");
        if (ddRenderer == null)
            return;

        DirectDrawWrapperManager manager = GameRendererBootstrap.Manager;
        IReadOnlyList<DirectDrawWrapper> renderers = manager.GetCompatibleRenderers();
        if (renderers.Count == 0)
        {
            Logger.Log("DisplayOptionsApplier: no compatible renderers from Renderers.ini.");
            return;
        }

        ddRenderer.SetComboItemEntries(renderers.Select(r => new ComboItemViewModel
        {
            Text = r.UIName,
            Tag = r.InternalName,
        }));

        string selected = RendererNameNormalizer.Normalize(UserINISettings.Instance.Renderer.Value);
        int index = renderers.ToList().FindIndex(r =>
            r.InternalName.Equals(selected, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            index = renderers.ToList().FindIndex(r => r == manager.SelectedRenderer);
        if (index < 0)
            index = 0;

        ddRenderer.SetSelectedIndexSilent(Math.Clamp(index, 0, renderers.Count - 1));
    }

    private static void LoadVisualStyleDropdown(UiNodeViewModel optionsRoot)
    {
        UiNodeViewModel? dd = FindVm(optionsRoot, "ddVisualStyle");
        if (dd == null)
            return;

        int index = Themes.DxThemeManager.IsTactical ? 1 : 0;
        dd.SetSelectedIndexSilent(index);
    }

    /// <summary>Reads the visual style currently selected in the Options dropdown.</summary>
    public static string ReadSelectedVisualStyle(UiNodeViewModel optionsRoot)
    {
        UiNodeViewModel? dd = FindVm(optionsRoot, "ddVisualStyle");
        if (dd == null)
            return Themes.DxThemeManager.CurrentStyle;

        return dd.SelectedIndex == 1
            ? Themes.DxThemeManager.StyleTactical
            : Themes.DxThemeManager.StyleDefault;
    }

    private static DirectDrawWrapper ResolveSelectedRenderer(UiNodeViewModel optionsRoot, DirectDrawWrapperManager manager)
    {
        UiNodeViewModel? ddRenderer = FindVm(optionsRoot, "ddRenderer");
        if (ddRenderer == null)
            return manager.SelectedRenderer;

        string? internalName = RendererNameNormalizer.Normalize(ReadRendererInternalName(ddRenderer));
        if (string.IsNullOrWhiteSpace(internalName))
            return manager.SelectedRenderer;

        return manager.GetCompatibleRenderers()
                   .FirstOrDefault(r => r.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase))
               ?? manager.SelectedRenderer;
    }

    private static string? ReadRendererInternalName(UiNodeViewModel ddRenderer)
    {
        int index = ddRenderer.SelectedIndex;
        if (index >= 0 && index < ddRenderer.ComboItemEntries.Count)
            return ddRenderer.ComboItemEntries[index].Tag ?? ddRenderer.ComboItems[index];

        if (index >= 0 && index < ddRenderer.ComboItems.Count)
            return ddRenderer.ComboItems[index];

        return null;
    }

    private static void LoadIngameResolution(UiNodeViewModel optionsRoot)
    {
        UiNodeViewModel? dd = FindVm(optionsRoot, "ddIngameResolution");
        if (dd == null || dd.ComboItems.Count == 0)
            return;

        string current = $"{UserINISettings.Instance.IngameScreenWidth.Value}x{UserINISettings.Instance.IngameScreenHeight.Value}";
        int index = dd.ComboItems.ToList().FindIndex(i => i.Equals(current, StringComparison.OrdinalIgnoreCase));
        dd.SetSelectedIndexSilent(index >= 0 ? index : 0);
    }

    private static void SaveIngameResolution(UiNodeViewModel optionsRoot)
    {
        UiNodeViewModel? dd = FindVm(optionsRoot, "ddIngameResolution");
        if (dd == null || dd.SelectedIndex < 0 || dd.SelectedIndex >= dd.ComboItems.Count)
            return;

        if (!TryParseResolution(dd.ComboItems[dd.SelectedIndex], out int width, out int height))
            return;

        UserINISettings.Instance.IngameScreenWidth.Value = width;
        UserINISettings.Instance.IngameScreenHeight.Value = height;
    }

    private static void LoadClientResolution(UiNodeViewModel optionsRoot)
    {
        UiNodeViewModel? dd = FindVm(optionsRoot, "ddClientResolution");
        if (dd == null)
            return;

        if (dd.ComboItems.Count == 0)
        {
            string items = Loading.OptionsDisplayControlsBootstrap.BuildClientResolutionItems();
            dd.SetComboItems(items.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (dd.ComboItems.Count == 0)
            return;

        string current = $"{UserINISettings.Instance.ClientResolutionX.Value}x{UserINISettings.Instance.ClientResolutionY.Value}";
        int index = dd.ComboItems.ToList().FindIndex(i =>
            i.Equals(current, StringComparison.OrdinalIgnoreCase));

        if (index < 0 && dd.ComboItems.Count > 0 && dd.ComboItems[0].StartsWith('('))
            index = 0;

        dd.SetSelectedIndexSilent(index >= 0 ? index : 0);
    }

    private static void SaveClientResolution(UiNodeViewModel optionsRoot)
    {
        UiNodeViewModel? dd = FindVm(optionsRoot, "ddClientResolution");
        if (dd == null || dd.SelectedIndex < 0 || dd.SelectedIndex >= dd.ComboItems.Count)
            return;

        string item = dd.ComboItems[dd.SelectedIndex];
        if (item.StartsWith('('))
            return;

        if (!TryParseResolution(item, out int width, out int height))
            return;

        UserINISettings.Instance.ClientResolutionX.Value = width;
        UserINISettings.Instance.ClientResolutionY.Value = height;
    }

    private static void SaveBackBuffer(UiNodeViewModel optionsRoot)
    {
        UiNodeViewModel? chk = FindVm(optionsRoot, "chkBackBufferInVRAM");
        if (chk == null)
            return;

        if (AppState.Configuration.Legacy.ClientGameType == ClientType.TS)
            UserINISettings.Instance.BackBufferInVRAM.Value = !chk.IsChecked;
        else
            UserINISettings.Instance.BackBufferInVRAM.Value = chk.IsChecked;
    }

    private static void LoadBackBuffer(UiNodeViewModel optionsRoot)
    {
        UiNodeViewModel? chk = FindVm(optionsRoot, "chkBackBufferInVRAM");
        if (chk == null)
            return;

        if (AppState.Configuration.Legacy.ClientGameType == ClientType.TS)
            chk.IsChecked = !UserINISettings.Instance.BackBufferInVRAM;
        else
            chk.IsChecked = UserINISettings.Instance.BackBufferInVRAM;
    }

    private static void SyncWindowedControlsFromRenderer(UiNodeViewModel root, DirectDrawWrapper renderer)
    {
        UiNodeViewModel? chkWindowed = FindVm(root, "chkWindowedMode");
        UiNodeViewModel? chkBorderless = FindVm(root, "chkBorderlessWindowedMode");
        if (chkWindowed == null)
            return;

        if (!renderer.UsesCustomWindowedOption())
        {
            chkWindowed.IsChecked = UserINISettings.Instance.WindowedMode;
            if (chkBorderless != null)
            {
                chkBorderless.IsChecked = UserINISettings.Instance.BorderlessWindowedMode;
                chkBorderless.IsEnabled = chkWindowed.IsChecked;
            }

            return;
        }

        var rendererSettingsIni = new IniFile(
            SafePath.CombineFilePath(AppState.Environment.GamePath, renderer.ConfigFileName));

        chkWindowed.IsChecked = rendererSettingsIni.GetBooleanValue(
            renderer.WindowedModeSection,
            renderer.WindowedModeKey,
            false);

        if (chkBorderless == null || string.IsNullOrEmpty(renderer.BorderlessWindowedModeKey))
            return;

        bool borderless = rendererSettingsIni.GetBooleanValue(
            renderer.WindowedModeSection,
            renderer.BorderlessWindowedModeKey,
            false);

        if (renderer.IsBorderlessWindowedModeKeyReversed)
            borderless = !borderless;

        chkBorderless.IsChecked = borderless;
        chkBorderless.IsEnabled = chkWindowed.IsChecked;
    }

    private static bool ReadCheck(UiNodeViewModel root, string id)
        => FindVm(root, id)?.IsChecked ?? false;

    private static bool TryParseResolution(string text, out int width, out int height)
    {
        width = 0;
        height = 0;
        string[] parts = text.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
               && int.TryParse(parts[0], out width)
               && int.TryParse(parts[1], out height);
    }

    private static UiNodeViewModel? FindVm(UiNodeViewModel root, string id)
    {
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = FindVm(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
