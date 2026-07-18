using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Services;
using ClientCore;
using ClientCore.I18N;
using ClientCore.PlatformShim;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>
/// Early client bootstrap aligned with DXMainClient <c>PreStartup.Initialize</c>.
/// Production UI defers GameRoot binding to <see cref="ModWorkspaceBinder"/> (workspace picker).
/// </summary>
public static class PreStartup
{
    private static bool _earlyRan;
    private static bool _fullBootstrapRan;

    public static StartupParams ParseArguments(string[] args)
    {
        bool noAudio = false;
        bool multipleInstanceMode = false;
        var unknown = new List<string>();

        foreach (string raw in args)
        {
            string argument = raw.ToUpperInvariant();
            switch (argument)
            {
                case "-NOAUDIO":
                    noAudio = true;
                    break;
                case "-MULTIPLEINSTANCE":
                    multipleInstanceMode = true;
                    break;
                default:
                    unknown.Add(raw);
                    break;
            }
        }

        return new StartupParams(noAudio, multipleInstanceMode, unknown);
    }

    /// <summary>
    /// Culture, exception handler, early logger — no GameRoot bind, no DX registry repair.
    /// </summary>
    public static void InitializeEarly(StartupParams parameters)
    {
        if (_earlyRan)
            return;

        _earlyRan = true;

        Translation.InitialUICulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(ProgramConstants.HARDCODED_LOCALE_CODE);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            HandleException(args.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception."));

        _ = EncodingExt.UTF8NoBOM;

        ClientLogService.EnsureEarlyInitialized();
        LogStartupParameters(parameters);
        Logger.Log("PreStartup: early init complete — waiting for workspace picker (no silent first-hit).");
    }

    /// <summary>
    /// Legacy / CLI path: resolve a game root and run full bootstrap immediately.
    /// Prefer <see cref="InitializeEarly"/> + <see cref="ModWorkspaceBinder"/> for the UI.
    /// Does <b>not</b> call <see cref="InstallationRegistry.TryRepairAllCandidates"/> (avoids DX key pollution).
    /// </summary>
    public static void Initialize(StartupParams parameters)
    {
        InitializeEarly(parameters);

        if (_fullBootstrapRan || ModWorkspaceBinder.IsBound)
            return;

        string gameRoot = ClientEnvironment.FindGameRoot(Directory.GetCurrentDirectory());
        string modName = ModRegistryCatalog.SuggestModName(gameRoot);
        string clientGameType = ModRegistryCatalog.ResolveClientGameTypeHint(modName, gameRoot) ?? "YR";
        if (!ModWorkspaceBinder.TryBindAndBootstrap(
                modName,
                gameRoot,
                clientGameType,
                out string? error))
        {
            Startup.BootstrapError = error ?? "Settings initialization failed.";
            Startup.BootstrapSucceeded = false;
            Logger.Log($"PreStartup: bootstrap failed: {Startup.BootstrapError}");
            return;
        }

        _fullBootstrapRan = true;
        FinishPostBindHousekeeping();
    }

    /// <summary>Backward-compatible entry used by CLI validators.</summary>
    public static void Initialize(string? gameRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(gameRoot))
        {
            Environment.CurrentDirectory = gameRoot;
            ProgramConstants.SetHostedGameRoot(gameRoot);
        }

        Initialize(ParseArguments([]));
    }

    /// <summary>Called after UI picker binds a workspace successfully.</summary>
    public static void NotifyWorkspaceBound()
    {
        _fullBootstrapRan = true;
        FinishPostBindHousekeeping();
    }

    private static void FinishPostBindHousekeeping()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ClientPermissions.EnsureWritableGameDirectory();

        RemoveObsoleteGameDirectoryFiles();
    }

    private static void LogStartupParameters(StartupParams parameters)
    {
        if (parameters.NoAudio)
        {
            Logger.Log("Startup parameter: No audio");
            throw new NotImplementedException("-NOAUDIO is not implemented in ClientAvalonia.");
        }

        if (parameters.MultipleInstanceMode)
            Logger.Log("Startup parameter: Allow multiple client instances");

        foreach (string unknown in parameters.UnknownStartupParams)
            Logger.Log("Unknown startup parameter: " + unknown);
    }

    private static void RemoveObsoleteGameDirectoryFiles()
    {
        DirectoryInfo gameDirectory = SafePath.GetDirectory(ProgramConstants.GamePath);
        gameDirectory.EnumerateFiles("mainclient.log").FirstOrDefault()?.Delete();
        gameDirectory.EnumerateFiles("aunchupdt.dat").FirstOrDefault()?.Delete();

        try
        {
            gameDirectory.EnumerateFiles("wsock32.dll").FirstOrDefault()?.Delete();
        }
        catch (Exception ex)
        {
            Logger.Log($"Deleting wsock32.dll failed: {ex.Message}");
        }
    }

    public static void HandleException(Exception ex)
    {
        Logger.Log("KABOOOOOOM!!! Info:");
        Logger.Log("Type: " + ex.GetType());
        Logger.Log("Message: " + ex.Message);
        Logger.Log("Stacktrace: " + ex.StackTrace);

        if (ex.InnerException != null)
        {
            Logger.Log("InnerException: " + ex.InnerException.Message);
            Logger.Log("Inner stacktrace: " + ex.InnerException.StackTrace);
        }

        try
        {
            string crashDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClientAvalonia",
                "ClientCrashLogs");
            Directory.CreateDirectory(crashDir);
            string crashPath = Path.Combine(crashDir, $"ClientCrashLog{DateTime.Now:yyyy_MM_dd_HH_mm}.txt");

            string? logFile = ProgramConstants.LogFileName;
            if (!string.IsNullOrWhiteSpace(logFile) && File.Exists(logFile))
            {
                File.Copy(logFile, crashPath, true);
                Logger.Log("Crash log copied to: " + crashPath);
            }
        }
        catch
        {
        }
    }
}
