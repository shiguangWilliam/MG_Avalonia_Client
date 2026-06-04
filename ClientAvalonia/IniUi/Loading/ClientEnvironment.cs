using ClientAvalonia.Core;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.Services;
using ClientCore;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// Discovers game root, active theme, client render size, and INI paths aligned with XNA Startup.
/// </summary>
public sealed class ClientEnvironment
{
    private ClientEnvironment(
        string gameRoot,
        string settingsFileName,
        string themeFolderPath,
        string? translationLocale,
        string translationsFolder,
        int clientRenderWidth,
        int clientRenderHeight)
    {
        GameRoot = gameRoot;
        SettingsFileName = settingsFileName;
        ThemeFolderPath = themeFolderPath.TrimEnd('/', '\\');
        TranslationLocale = translationLocale;
        TranslationsFolder = translationsFolder.TrimEnd('/', '\\');
        ClientRenderWidth = clientRenderWidth;
        ClientRenderHeight = clientRenderHeight;
    }

    public string GameRoot { get; }

    /// <summary>User settings INI file name in game root, e.g. RA2MG.ini.</summary>
    public string SettingsFileName { get; }

    public string UserSettingsPath => Path.Combine(GameRoot, SettingsFileName);

    /// <summary>Relative to Resources/, e.g. ThemeMG/</summary>
    public string ThemeFolderPath { get; }

    public string? TranslationLocale { get; }

    /// <summary>Relative to game root, e.g. Resources/Translations</summary>
    public string TranslationsFolder { get; }

    /// <summary>User client resolution clamped to ClientDefinitions min/max render size.</summary>
    public int ClientRenderWidth { get; }

    public int ClientRenderHeight { get; }

    public string ResourcesDirectory => Path.Combine(GameRoot, "Resources");

    public string ThemeResourceDirectory => Path.Combine(ResourcesDirectory, ThemeFolderPath);

    public static ClientEnvironment Discover(string? startDirectory = null)
    {
        startDirectory ??= Directory.GetCurrentDirectory();
        string gameRoot = FindGameRoot(startDirectory);

        if (ClientCoreBootstrap.TryEnsureInitialized(gameRoot, out _))
            return DiscoverFromCore(gameRoot);

        return DiscoverLegacy(gameRoot);
    }

    private static ClientEnvironment DiscoverFromCore(string gameRoot)
    {
        ClientConfiguration config = ClientConfiguration.Instance;
        UserINISettings settings = UserINISettings.Instance;

        string themeFolder = settings.ThemeFolderPath.TrimEnd('/', '\\');
        string settingsFileName = config.SettingsIniName;
        string? translationLocale = string.IsNullOrWhiteSpace(settings.Translation.Value)
            ? null
            : settings.Translation.Value;
        string translationsFolder = config.TranslationsFolderPath;

        int width = settings.GetValue(UserINISettings.VIDEO, "ClientResolutionX", 0);
        int height = settings.GetValue(UserINISettings.VIDEO, "ClientResolutionY", 0);

        if (width <= 0 || height <= 0)
        {
            string? mainMenuIni = ResolveWindowIniPath(gameRoot, themeFolder, "MainMenu");
            if (mainMenuIni != null && ReadWindowSize(mainMenuIni, "MainMenu") is { } menuSize)
            {
                width = menuSize.Width;
                height = menuSize.Height;
            }
        }

        if (width <= 0)
            width = config.MinimumRenderWidth;
        if (height <= 0)
            height = config.MinimumRenderHeight;

        width = Math.Clamp(width, config.MinimumRenderWidth, config.MaximumRenderWidth);
        height = Math.Clamp(height, config.MinimumRenderHeight, config.MaximumRenderHeight);

        return new ClientEnvironment(
            gameRoot,
            settingsFileName,
            themeFolder,
            translationLocale,
            translationsFolder,
            width,
            height);
    }

    private static ClientEnvironment DiscoverLegacy(string gameRoot)
    {
        IniDocument? clientDefs = TryLoadClientDefinitions(gameRoot);

        string settingsFileName = clientDefs?.GetSection("Settings")?.GetStringValue("SettingsFile", "Settings.ini")
            ?? "Settings.ini";
        IniDocument? userSettings = TryLoadIni(Path.Combine(gameRoot, settingsFileName));

        string themeName = userSettings?.GetSection("MultiPlayer")?.GetStringValue("Theme", string.Empty) ?? string.Empty;
        string? themeFolder = ResolveThemeFolder(clientDefs, themeName)
            ?? "DTA";

        string translationsFolder = clientDefs?.GetSection("Translations")?.GetStringValue("TranslationsFolder", "Resources/Translations")
            ?? "Resources/Translations";
        if (!translationsFolder.StartsWith("Resources", StringComparison.OrdinalIgnoreCase))
            translationsFolder = Path.Combine("Resources", translationsFolder);

        string? translationLocale = userSettings?.GetSection("Options")?.GetStringValue("Translation", string.Empty);
        if (string.IsNullOrWhiteSpace(translationLocale))
            translationLocale = null;

        (int width, int height) = ResolveClientRenderSize(clientDefs, userSettings, gameRoot, themeFolder);

        return new ClientEnvironment(gameRoot, settingsFileName, themeFolder, translationLocale, translationsFolder, width, height);
    }

    /// <summary>Layout viewport: window INI Size first, else user client resolution.</summary>
    public LayoutContext CreateLayoutContextForWindow(string iniPath, string windowSectionName)
    {
        if (FloatingOverlayLayout.IsOverlayWindow(windowSectionName))
        {
            (int overlayWidth, int overlayHeight) = FloatingOverlayLayout.ResolveOverlaySize(iniPath, windowSectionName);
            return new LayoutContext(overlayWidth, overlayHeight, ParserConstantsLoader.LoadForGame(GameRoot));
        }

        (int width, int height) = ReadWindowSize(iniPath, windowSectionName)
            ?? (ClientRenderWidth, ClientRenderHeight);
        return new LayoutContext(width, height, ParserConstantsLoader.LoadForGame(GameRoot));
    }

    public static (int Width, int Height)? ReadWindowSize(string iniPath, string windowSectionName)
    {
        if (!File.Exists(iniPath))
            return null;

        IniDocument doc = IniDocument.Load(iniPath);
        IniSection? section = doc.GetSection(windowSectionName);
        if (section == null)
            return null;

        string sizeValue = section.GetStringValue("Size", string.Empty);
        if (string.IsNullOrWhiteSpace(sizeValue))
            return null;

        string[] parts = sizeValue.Split(',');
        if (parts.Length != 2)
            return null;

        if (!int.TryParse(parts[0].Trim(), out int width) || !int.TryParse(parts[1].Trim(), out int height))
            return null;

        return width > 0 && height > 0 ? (width, height) : null;
    }

    private static (int Width, int Height) ResolveClientRenderSize(
        IniDocument? clientDefs,
        IniDocument? userSettings,
        string gameRoot,
        string themeFolder)
    {
        int minW = ReadIntSetting(clientDefs, "Settings", "MinimumRenderWidth", 1280);
        int minH = ReadIntSetting(clientDefs, "Settings", "MinimumRenderHeight", 720);
        int maxW = ReadIntSetting(clientDefs, "Settings", "MaximumRenderWidth", 1280);
        int maxH = ReadIntSetting(clientDefs, "Settings", "MaximumRenderHeight", 720);

        int width = ReadIntSetting(userSettings, "Video", "ClientResolutionX", 0);
        int height = ReadIntSetting(userSettings, "Video", "ClientResolutionY", 0);

        if (width <= 0 || height <= 0)
        {
            string? mainMenuIni = ResolveWindowIniPath(gameRoot, themeFolder, "MainMenu");
            if (mainMenuIni != null && ReadWindowSize(mainMenuIni, "MainMenu") is { } menuSize)
            {
                width = menuSize.Width;
                height = menuSize.Height;
            }
        }

        if (width <= 0)
            width = minW;
        if (height <= 0)
            height = minH;

        width = Math.Clamp(width, minW, maxW);
        height = Math.Clamp(height, minH, maxH);
        return (width, height);
    }

    private static int ReadIntSetting(IniDocument? doc, string sectionName, string key, int defaultValue)
    {
        if (doc == null)
            return defaultValue;

        string value = doc.GetSection(sectionName)?.GetStringValue(key, string.Empty) ?? string.Empty;
        return int.TryParse(value.Trim(), out int parsed) ? parsed : defaultValue;
    }

    /// <summary>Asset search paths in XNA priority order (first match wins).</summary>
    public IEnumerable<string> GetAssetSearchPaths()
    {
        if (ClientCoreBootstrap.IsInitialized)
            yield return ProgramConstants.GetResourcePath();

        if (!string.IsNullOrEmpty(TranslationLocale))
        {
            yield return Path.Combine(GameRoot, TranslationsFolder, TranslationLocale, ThemeFolderPath);
            yield return Path.Combine(GameRoot, TranslationsFolder, TranslationLocale);
        }

        yield return ThemeResourceDirectory;
        yield return ResourcesDirectory;
        yield return GameRoot;
        yield return Path.Combine(ResourcesDirectory, "DTA");
        yield return Path.Combine(ResourcesDirectory, "DTA", "Default Theme");
    }

    /// <summary>Window INI resolution: theme MainMenu.ini → Resources/MainMenu.ini → DTA fallback.</summary>
    public string? ResolveWindowIni(string windowName)
        => ResolveWindowIniPath(GameRoot, ThemeFolderPath, windowName);

    private static string? ResolveWindowIniPath(string gameRoot, string themeFolderPath, string windowName)
    {
        string resourcesDirectory = Path.Combine(gameRoot, "Resources");
        string themeResourceDirectory = Path.Combine(resourcesDirectory, themeFolderPath.TrimEnd('/', '\\'));
        string fileName = $"{windowName}.ini";
        string[] candidates =
        [
            Path.Combine(themeResourceDirectory, fileName),
            Path.Combine(resourcesDirectory, fileName),
            Path.Combine(resourcesDirectory, "DTA", fileName),
        ];

        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public static string FindGameRoot(string startDirectory)
    {
        string current = Path.GetFullPath(startDirectory);
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(current, "Resources", "ClientDefinitions.ini")))
                return current;

            if (Directory.Exists(Path.Combine(current, "Resources"))
                && Directory.EnumerateFiles(Path.Combine(current, "Resources"), "*.ini").Any())
                return current;

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null)
                break;
            current = parent.FullName;
        }

        return Path.GetFullPath(startDirectory);
    }

    private static IniDocument? TryLoadClientDefinitions(string gameRoot)
    {
        string path = Path.Combine(gameRoot, "Resources", "ClientDefinitions.ini");
        return File.Exists(path) ? IniDocument.Load(path) : null;
    }

    private static IniDocument? TryLoadIni(string path)
        => File.Exists(path) ? IniDocument.Load(path) : null;

    private static string? ResolveThemeFolder(IniDocument? clientDefs, string themeName)
    {
        IniSection? themes = clientDefs?.GetSection("Themes");
        if (themes == null)
            return null;

        if (!string.IsNullOrWhiteSpace(themeName))
        {
            foreach (KeyValuePair<string, string> entry in themes.Keys)
            {
                string[] parts = entry.Value.Split(',');
                if (parts.Length >= 2 && parts[0].Trim().Equals(themeName, StringComparison.OrdinalIgnoreCase))
                    return parts[1].Trim();
            }
        }

        foreach (KeyValuePair<string, string> entry in themes.Keys.OrderBy(k => int.TryParse(k.Key, out int i) ? i : int.MaxValue))
        {
            string[] parts = entry.Value.Split(',');
            if (parts.Length >= 2)
                return parts[1].Trim();
        }

        return null;
    }
}
