using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Configuration;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.GlobalState.Updater;
using ClientAvalonia.IniUi.Actions;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using ClientCore;
using ClientCore.I18N;
using ClientCore.PlatformShim;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>
/// Early client bootstrap aligned with DXMainClient <c>PreStartup.Initialize</c>.
/// Main branch: MG-only registry self-check, then direct start (no multi-mod picker).
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

        _ = EncodingExt.UTF8NoBOM;

        // Boot self-check (MG only):
        // missing key → write launcher CWD; stale key (no dir / no gamemd.exe) → rewrite CWD.
        string launcherCwd = Directory.GetCurrentDirectory();
        string gameRoot = InstallationRegistry.ResolveAndHealMgInstallPath(launcherCwd);

        // Prefer a root that can actually host the client UI (ClientDefinitions.ini).
        // Registry heal already fixed InstallPath; walk-up covers cwd that is a subfolder.
        if (!InstallationRegistry.IsInstallPathValid(gameRoot))
            gameRoot = ClientEnvironment.FindGameRoot(gameRoot);

        Environment.CurrentDirectory = gameRoot;
        ProgramConstants.SetHostedGameRoot(gameRoot);
        Logger.Log($"PreStartup: MG game root resolved = {gameRoot}");

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

        // D2: register environment services BEFORE Startup.Execute so any code
        // inside Startup that calls EnvironmentServices.Resolve<T>() finds the
        // factories. The previous order (Register after Execute) worked only by
        // accident because Startup currently doesn't resolve anything during
        // bootstrap — but the implicit dependency is fragile.
        RegisterEnvironmentServices();

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

    /// <summary>Registers L1 domain interfaces for Resolve&lt;T&gt; injection.</summary>
    private static void RegisterEnvironmentServices()
    {
        EnvironmentServices.Register<IGameEnvironment>(() => new ProgramConstantsGameEnvironment());
        EnvironmentServices.Register<IGameConfiguration>(() => new ClientConfigurationAdapter());
        EnvironmentServices.Register<ICnCNetSession>(() => new CnCNetSessionServiceAdapter());
        EnvironmentServices.Register<IResourceCatalog>(
            () => new GameResourceCatalogAdapter(GameResourceCatalog.Instance));
        EnvironmentServices.Register<IResourceManifest>(() => new NoOpResourceManifest());
        EnvironmentServices.Register<IUpdater>(() => new UpdaterAdapter());
        EnvironmentServices.Register<IMultiplayerColorCatalog>(() => new MultiplayerColorCatalogAdapter());
        EnvironmentServices.Register<ILobbyCatalogService>(() => LobbyCatalogService.Instance);
        EnvironmentServices.Register<ISkirmishSettingsService>(() => new SkirmishSettingsService());

        // INI 动作目录：注册内置动作一次（启动期完成），后续窗口导航时由
        // IniBehaviorApplier 派发。Mod 可在 INI 写 $LeftClickAction=ExitApplication
        // 把任意按钮绑到这些动作上。
        var iniCatalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(iniCatalog);
        EnvironmentServices.Register<IIniActionCatalog>(() => iniCatalog);
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
            // DX 降级行为：记录后继续启动（ClientAvalonia 音频子系统无独立开关，
            // 保持不动即等效"禁用失败但不致命"）。崩溃退出比上游更糟。
            Logger.Log("Startup parameter: No audio supplied; audio subsystem unchanged (-NOAUDIO is not implemented).");
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

        // DX 启动器靠 BootstrapError 显示错误对话框——不设值时 Release 下
        // 崩溃对话框消息为空，用户无从上报。此处兜底写全类型+消息。
        try
        {
            Startup.BootstrapError ??= $"{ex.GetType().Name}: {ex.Message}";
            Startup.BootstrapSucceeded = false;
        }
        catch
        {
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
