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
/// </summary>
public static class PreStartup
{
    private static bool _ran;

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

    public static void Initialize(StartupParams parameters)
    {
        if (_ran)
            return;

        _ran = true;

        Translation.InitialUICulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(ProgramConstants.HARDCODED_LOCALE_CODE);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            HandleException(args.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception."));

        string gameRoot = ClientEnvironment.FindGameRoot(Directory.GetCurrentDirectory());
        Environment.CurrentDirectory = gameRoot;
        ProgramConstants.SetHostedGameRoot(gameRoot);

        _ = EncodingExt.UTF8NoBOM;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ClientPermissions.EnsureWritableGameDirectory();

        ClientLogService.EnsureInitialized();

        if (!ClientCoreBootstrap.TryEnsureInitialized(gameRoot, out string? settingsError))
        {
            Startup.BootstrapError = settingsError ?? "Settings initialization failed.";
            Startup.BootstrapSucceeded = false;
            Logger.Log($"PreStartup: bootstrap failed: {Startup.BootstrapError}");
            return;
        }

        LogStartupParameters(parameters);

        RemoveObsoleteGameDirectoryFiles();

        var startup = new Startup();
#if DEBUG
        startup.Execute();
#else
        try
        {
            startup.Execute();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
#endif
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
            string crashDir = SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, "ClientCrashLogs");
            Directory.CreateDirectory(crashDir);
            string crashPath = SafePath.CombineFilePath(
                crashDir,
                $"ClientCrashLog{DateTime.Now:yyyy_MM_dd_HH_mm}.txt");
            File.Copy(SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, "client.log"), crashPath, true);
            Logger.Log("Crash log copied to: " + crashPath);
        }
        catch
        {
        }
    }
}
