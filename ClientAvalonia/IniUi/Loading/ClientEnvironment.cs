// ClientEnvironment: game-root discovery, theme resolution, and asset/INI search paths.
// The search path order in GetAssetSearchPaths / ResolveNamedIni is aligned with DX's
// AppState.Environment.ResourcesPath. Read ClientAvalonia/IniUi/README.md 搂ResourceResolver
// before changing path order —?map previews and side icons rely on GameRoot being a root.
using ClientAvalonia.IniUi;
using ClientAvalonia.Core;
using ClientAvalonia.GlobalState;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.Services;
using ClientCore;
using Rampastring.Tools;

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
        ClientConfiguration config = AppState.Configuration.Legacy;
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
            string? mainMenuIni = ResolveWindowIniPath(gameRoot, themeFolder, WindowKind.MainMenu);
            if (mainMenuIni != null && ReadWindowSize(mainMenuIni, WindowKind.MainMenu) is { } menuSize)
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

        // Campaign is a full-bleed root panel: keep the client resolution so the
        // shared Earth backdrop and HUD columns fill the same viewport as MainMenu.
        if (FloatingOverlayLayout.IsCampaignWindow(windowSectionName))
            return new LayoutContext(ClientRenderWidth, ClientRenderHeight, ParserConstantsLoader.LoadForGame(GameRoot));

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
            string? mainMenuIni = ResolveWindowIniPath(gameRoot, themeFolder, WindowKind.MainMenu);
            if (mainMenuIni != null && ReadWindowSize(mainMenuIni, WindowKind.MainMenu) is { } menuSize)
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
            yield return AppState.Environment.ResourcesPath;

        if (!string.IsNullOrEmpty(TranslationLocale))
        {
            yield return Path.Combine(GameRoot, TranslationsFolder, TranslationLocale, ThemeFolderPath);
            yield return Path.Combine(GameRoot, TranslationsFolder, TranslationLocale);
        }

        yield return ThemeResourceDirectory;
        yield return ResourcesDirectory;
        // Legacy DTA layouts may still ship shared files under Resources/Base.
        yield return Path.Combine(ResourcesDirectory, "Base");
        // Backward compat: published DTA resource bundles (pre-2.12 standard) ship under Resources/DTA/.
        yield return Path.Combine(ResourcesDirectory, "DTA");
        yield return Path.Combine(ResourcesDirectory, "DTA", "Default Theme");
        // GameRoot must remain in the search roots: map previews, side icons and other game-relative
        // assets are addressed as paths like "Maps/Fan-made/xxx.png" or "Previews/foo.png" —?i.e.
        // relative to the game root, NOT to Resources/. Removing this root silently breaks
        // GameAssetResolver.ResolveMapPreviewRelativePath on every mod (MG/LNOD/QEC alike).
        yield return GameRoot;
    }

    /// <summary>Window INI resolution: theme MainMenu.ini 鈫?Resources/MainMenu.ini 鈫?DTA fallback.</summary>
    public string? ResolveWindowIni(string windowName)
        => ResolveWindowIniPath(GameRoot, ThemeFolderPath, windowName);

    /// <summary>GenericWindow.ini in theme / Resources / DTA.</summary>
    public string? ResolveGenericWindowIni()
        => ResolveNamedIni("GenericWindow.ini");

    /// <summary>Dedicated window INI when it contains a usable section; else [section] in GenericWindow.ini.</summary>
    public (string IniPath, string Section)? TryResolveOverlaySection(string windowName, string genericSectionName)
    {
        string? dedicated = ResolveWindowIni(windowName);
        if (dedicated != null)
        {
            try
            {
                IniDocument doc = IniDocument.Load(dedicated);
                string section = ResolveIniSectionForWindow(doc, windowName);
                if (doc.GetSection(section) != null)
                    return (dedicated, section);
            }
            catch
            {
                // Fall through to GenericWindow.ini (same as DX when a dedicated file is unusable).
            }
        }

        string? generic = ResolveGenericWindowIni();
        if (generic == null || !File.Exists(generic))
            return null;

        IniDocument genericDoc = IniDocument.Load(generic);
        if (genericDoc.GetSection(genericSectionName) == null)
            return null;

        return (generic, genericSectionName);
    }

    /// <summary>
    /// Control-driven INI resolution (aligned with ClientGUI/INItializableWindow + XNAWindow):
    /// returns the window INI path together with the <em>INI section</em> to load as the root.
    /// Navigation may use logical names like <c>CnCNetGameLobby</c>, but MG's INI chain puts the
    /// real window attributes on <c>[MultiplayerGameLobby]</c> (via BasedOn). Loading with the
    /// logical name falls back to <c>[GenericWindow]</c> and treats <c>[MultiplayerGameLobby]</c>
    /// as a foreign lobby (skipped), leaving a black viewport with a few orphan toolbar scraps.
    /// </summary>
    public (string IniPath, string SectionName)? ResolveWindowLoadTarget(string windowName)
    {
        string? iniPath = ResolveWindowIni(windowName);
        if (iniPath == null && IsGameLobbyWindowName(windowName))
        {
            iniPath = ResolveWindowIni(WindowKind.MultiplayerGameLobby)
                ?? ResolveWindowIni(WindowKind.SkirmishLobby);
        }

        // Last-resort fallback: if neither a dedicated window INI nor a lobby INI exists,
        // try GenericWindow.ini (XNAWindow.SetAttributesFromIni() generic fallback).
        iniPath ??= ResolveGenericWindowIni();
        if (iniPath == null)
            return null;

        string sectionName = ResolveWindowIniSection(iniPath, windowName);
        return (iniPath, sectionName);
    }

    /// <summary>
    /// Map logical window names onto the INI section that actually carries Size / $CC /
    /// $BaseSection (DX IniNameOverride vs Name). Prefer an exact section match; else aliases.
    /// </summary>
    private static string ResolveWindowIniSection(string iniPath, string windowName)
    {
        try
        {
            IniDocument doc = IniDocument.Load(iniPath);
            return ResolveIniSectionForWindow(doc, windowName);
        }
        catch
        {
            // Fall through to the navigation name; LoadWindow still has GenericWindow fallback.
        }

        return windowName;
    }

    private static string ResolveIniSectionForWindow(IniDocument doc, string windowName)
    {
        if (doc.GetSection(windowName) != null)
            return windowName;

        foreach ((string logical, string section) in WindowSectionAliases)
        {
            if (windowName.Equals(logical, StringComparison.OrdinalIgnoreCase)
                && doc.GetSection(section) != null)
            {
                return section;
            }
        }

        return windowName;
    }

    /// <summary>
    /// DX-shaped IniNameOverride 鈫?Name aliases. Keep navigation / behaviors on the logical name;
    /// only the load target section is remapped.
    /// </summary>
    private static readonly (string LogicalName, string SectionName)[] WindowSectionAliases =
    [
        (WindowKind.CnCNetGameLobby, WindowKind.MultiplayerGameLobby),
        (WindowKind.LanGameLobby, WindowKind.MultiplayerGameLobby),
        ("CnCNetGameLoadingLobby", "GameLoadingLobby"),
        ("LANGameLoadingLobby", "GameLoadingLobby"),
    ];

    private static bool IsGameLobbyWindowName(string windowName)
        => windowName.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase)
           || windowName.Equals(WindowKind.LanGameLobby, StringComparison.OrdinalIgnoreCase)
           || windowName.Equals(WindowKind.MultiplayerGameLobby, StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("CnCNetGameLoadingLobby", StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("LANGameLoadingLobby", StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("GameLoadingLobby", StringComparison.OrdinalIgnoreCase);

    private string? ResolveNamedIni(string fileName)
    {
        string resourcesDirectory = Path.Combine(GameRoot, "Resources");
        string themeResourceDirectory = Path.Combine(resourcesDirectory, ThemeFolderPath.TrimEnd('/', '\\'));
        // Resolution order mirrors ClientGUI/INItializableWindow.GetConfigPath() +
        // XNAWindow.SetAttributesFromIni(), with an extra DTA/ tier for legacy mod bundles
        // that still ship their fallback INIs under Resources/DTA/ (pre-2.12 convention).
        string[] candidates =
        [
            Path.Combine(themeResourceDirectory, fileName),
            Path.Combine(resourcesDirectory, fileName),
            Path.Combine(resourcesDirectory, "Base", fileName),
            Path.Combine(resourcesDirectory, "DTA", fileName),
        ];

        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string? ResolveWindowIniPath(string gameRoot, string themeFolderPath, string windowName)
    {
        string resourcesDirectory = Path.Combine(gameRoot, "Resources");
        string themeResourceDirectory = Path.Combine(resourcesDirectory, themeFolderPath.TrimEnd('/', '\\'));
        string fileName = $"{windowName}.ini";
        // Resolution order mirrors ClientGUI/INItializableWindow.GetConfigPath() +
        // XNAWindow.SetAttributesFromIni(), with an extra DTA/ tier for legacy mod bundles.
        string[] candidates =
        [
            Path.Combine(themeResourceDirectory, fileName),
            Path.Combine(resourcesDirectory, fileName),
            Path.Combine(resourcesDirectory, "Base", fileName),
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
        => FindGameRoot(startDirectory, registryCandidates: null);

    /// <summary>
    /// Walk CWD / exe only —?no registry first-hit. Used by the workspace picker local probe.
    /// </summary>
    public static string? TryFindGameRootWithoutRegistry(string? startDirectory = null)
    {
        startDirectory ??= Directory.GetCurrentDirectory();

        string? fromStart = WalkUpForGameRoot(Path.GetFullPath(startDirectory));
        if (fromStart != null)
            return fromStart;

        return WalkUpForGameRoot(AppContext.BaseDirectory);
    }

    /// <summary>Test seam: <paramref name="registryCandidates"/> overrides the hard-coded list.</summary>
    internal static string FindGameRoot(string startDirectory, string[]? registryCandidates)
    {
        // Bootstrap priority (highest first):
        //   1. MG registry hint (HKCU\SOFTWARE\MomentOfGenesis\InstallPath + gamemd.exe)
        //   2. Walk upward from <startDirectory> (usually CWD)
        //   3. Walk upward from AppContext.BaseDirectory (exe folder)

        string? registryRoot = registryCandidates != null
            ? InstallationRegistry.TryReadEarlyBoundInstallPath(registryCandidates, validateFilePresence: true)
            : InstallationRegistry.TryReadEarlyBoundInstallPath(validateFilePresence: true);
        if (!string.IsNullOrWhiteSpace(registryRoot))
            return registryRoot!;

        string? fromStart = WalkUpForGameRoot(Path.GetFullPath(startDirectory));
        if (fromStart != null)
            return fromStart;

        string? fromExe = WalkUpForGameRoot(AppContext.BaseDirectory);
        if (fromExe != null)
            return fromExe;

        Logger.Log($"ClientEnvironment: FindGameRoot could not locate Resources/ClientDefinitions.ini from start='{startDirectory}' or exe='{AppContext.BaseDirectory}'.");
        return Path.GetFullPath(startDirectory);
    }

    private static string? WalkUpForGameRoot(string start)
    {
        string current = start;
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

        return null;
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
