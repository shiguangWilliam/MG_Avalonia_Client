using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using ClientAvalonia.Configuration;
using ClientAvalonia.Core;
using ClientAvalonia.Domain;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Loading;
using ClientCore;
using ClientCore.Extensions;
using ClientCore.INIProcessing;
using ClientCore.Settings;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>
/// Launches the mod game executable (DXMainClient <c>ClientGUI.GameProcessLogic</c>).
/// Single implementation shared by skirmish, CnCNet, LAN, and campaign modes.
/// </summary>
public static class GameProcessLauncher
{
    public static event Action? GameProcessStarting;
    public static event Action? GameProcessStarted;
    public static event Action? GameProcessExited;

    public static bool UseQres { get; set; }
    public static bool SingleCoreAffinity { get; set; }

    public static bool TryStart(
        ClientEnvironment environment,
        Window? errorOwner,
        out Process? process,
        out string message,
        out long preparationElapsedMs)
    {
        process = null;
        message = string.Empty;
        preparationElapsedMs = 0;

        if (!ClientCoreBootstrap.IsInitialized)
            ClientCoreBootstrap.EnsureInitialized(environment.GameRoot);

        Logger.Log("About to launch main game executable.");

        if (!WaitForIniPreprocessor(errorOwner))
        {
            message = "INI preprocessing not complete.";
            return false;
        }

        IGameConfiguration config = EnvironmentServices.Resolve<IGameConfiguration>();
        string gamePath = environment.GameRoot;
        // GetOperatingSystemVersion / ExtraExeCommandLineParameters / UnixGameExecutableName 未抽接口
        ClientConfiguration? coreConfig = (config as ClientConfigurationAdapter)?.Legacy;
        OSVersion osVersion = coreConfig?.GetOperatingSystemVersion() ?? AppState.Configuration.Legacy.GetOperatingSystemVersion();
        string gameExecutableName = config.GetGameExecutableName();
        if (string.IsNullOrWhiteSpace(gameExecutableName))
        {
            message = "Game executable is not configured (GameExecutableNames in ClientDefinitions.ini).";
            ClientDialogService.ShowError(errorOwner, "Error launching game", message);
            return false;
        }

        ResolveLaunchExecutables(osVersion, gameExecutableName, out string launchExecutableName, out string additionalExecutableName);

        string extraCommandLine = coreConfig?.ExtraExeCommandLineParameters?.Trim()
                                  ?? AppState.Configuration.Legacy.ExtraExeCommandLineParameters?.Trim()
                                  ?? string.Empty;

        SafePath.DeleteFileIfExists(gamePath, "DTA.LOG");
        SafePath.DeleteFileIfExists(gamePath, "TI.LOG");
        SafePath.DeleteFileIfExists(gamePath, "TS.LOG");

        preparationElapsedMs = GameLaunchPreparation.PrepareForLaunch();
        GameProcessStarting?.Invoke();

        bool windowed = GameRendererBootstrap.Manager.GetEffectiveWindowedMode();
        bool qresAvailable = File.Exists(SafePath.CombineFilePath(gamePath, ProgramConstants.QRES_EXECUTABLE));
        bool useQres = GameLaunchPolicy.ShouldUseQres(
            GameLaunchPolicy.IsWindowsPlatform(),
            qresAvailable,
            UseQres,
            windowed);

        if (useQres)
        {
            process = StartViaQres(launchExecutableName, additionalExecutableName, extraCommandLine, windowed, errorOwner, out message);
        }
        else
        {
            if (!windowed && !qresAvailable)
                Logger.Log("qres.dat not found — fullscreen relies on renderer or Windows compatibility for 16-bit color.");
            process = StartDirect(launchExecutableName, additionalExecutableName, extraCommandLine, errorOwner, out message);
        }

        if (process == null)
            return false;

        GameProcessStarted?.Invoke();
        Logger.Log("Waiting for qres.dat or " + launchExecutableName + " to exit.");
        message = $"Launched {launchExecutableName}";
        return true;
    }

    public static void AttachExitHandler(Process process)
    {
        process.EnableRaisingEvents = true;
        process.Exited += OnProcessExited;
    }

    private static void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is Process proc)
        {
            proc.Exited -= OnProcessExited;
            try
            {
                int code = proc.ExitCode;
                Logger.Log($"GameProcessLauncher: exit code {code} (0x{code & 0xFFFFFFFF:X8}).");
            }
            catch (Exception ex)
            {
                Logger.Log($"GameProcessLauncher: reading exit code failed: {ex.Message}");
            }

            try
            {
                proc.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Log($"GameProcessLauncher: disposing process handle failed: {ex.Message}");
            }
        }

        Logger.Log("GameProcessLauncher: process exited.");
        GameProcessExited?.Invoke();
    }

    private static void ResolveLaunchExecutables(
        OSVersion osVersion,
        string gameExecutableName,
        out string launchExecutableName,
        out string additionalExecutableName)
    {
        additionalExecutableName = string.Empty;

        if (osVersion == OSVersion.UNIX)
        {
            // UnixGameExecutableName 未抽接口，escape hatch
            IGameConfiguration config = EnvironmentServices.Resolve<IGameConfiguration>();
            launchExecutableName = (config as ClientConfigurationAdapter)?.Legacy?.UnixGameExecutableName
                                  ?? AppState.Configuration.Legacy.UnixGameExecutableName;
            if (string.IsNullOrWhiteSpace(launchExecutableName))
                launchExecutableName = gameExecutableName;
            return;
        }

        IGameConfiguration cfg = EnvironmentServices.Resolve<IGameConfiguration>();
        string launcherExecutableName = cfg.GameLauncherExecutableName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(launcherExecutableName))
        {
            launchExecutableName = gameExecutableName;
            return;
        }

        launchExecutableName = launcherExecutableName;
        additionalExecutableName = QuoteArgument(gameExecutableName) + " ";
    }

    private static Process? StartDirect(
        string launchExecutableName,
        string additionalExecutableName,
        string extraCommandLine,
        Window? errorOwner,
        out string message)
    {
        message = string.Empty;
        string arguments = BuildSpawnArguments(additionalExecutableName, extraCommandLine);

        if (!ValidateSyringeConfiguration(launchExecutableName, additionalExecutableName, arguments, errorOwner, out message))
            return null;

        string gamePath = EnvironmentServices.Resolve<IGameEnvironment>().GamePath;
        FileInfo gameFileInfo = SafePath.GetFile(gamePath, launchExecutableName);
        if (!gameFileInfo.Exists)
        {
            message = $"Launch executable not found: {gameFileInfo.FullName}";
            ClientDialogService.ShowError(errorOwner, "Error launching game", message);
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = gameFileInfo.FullName,
            Arguments = arguments,
            WorkingDirectory = gamePath,
            UseShellExecute = false,
        };

        Logger.Log("Launch executable: " + startInfo.FileName);
        Logger.Log("Launch arguments: " + startInfo.Arguments);

        Process process;
        var startSw = Stopwatch.StartNew();
        try
        {
            process = StartGameProcess(startInfo);
            startSw.Stop();
            Logger.Log($"GameProcessLauncher: Process.Start returned in {startSw.ElapsedMilliseconds} ms (pid={process.Id}).");
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Logger.Log("Error launching " + gameFileInfo.Name + ": " + ex);
            ClientDialogService.ShowError(
                errorOwner,
                "Error launching game",
                string.Format(
                    "Error launching {0}. Please check that your anti-virus isn't blocking the CnCNet Client.\n\nReturned error: {1}",
                    gameFileInfo.Name,
                    ex.Message));
            return null;
        }

        if ((RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            && Environment.ProcessorCount > 1
            && SingleCoreAffinity)
        {
            process.ProcessorAffinity = (IntPtr)2;
        }

        return process;
    }

    private static Process? StartViaQres(
        string launchExecutableName,
        string additionalExecutableName,
        string extraCommandLine,
        bool windowed,
        Window? errorOwner,
        out string message)
    {
        message = string.Empty;
        Logger.Log(windowed ? "Windowed mode is enabled - using QRes." : "Fullscreen launch - using QRes for 16-bit color depth.");

        string gamePath = EnvironmentServices.Resolve<IGameEnvironment>().GamePath;
        string spawnArgs = BuildSpawnArguments(additionalExecutableName, extraCommandLine);
        string target = QuoteArgument(SafePath.CombineFilePath(gamePath, launchExecutableName));
        string qresArgs = string.IsNullOrWhiteSpace(extraCommandLine)
            ? $"c=16 /R {target} {spawnArgs.Trim()}"
            : $"c=16 /R {target} {spawnArgs.Trim()}";

        string qresPath = SafePath.CombineFilePath(gamePath, ProgramConstants.QRES_EXECUTABLE);
        var startInfo = new ProcessStartInfo
        {
            FileName = ProgramConstants.QRES_EXECUTABLE,
            Arguments = qresArgs,
            WorkingDirectory = gamePath,
            UseShellExecute = false,
        };

        Logger.Log("Launch executable: " + qresPath);
        Logger.Log("Launch arguments: " + startInfo.Arguments);

        if (!File.Exists(qresPath))
        {
            message = $"QRes helper not found: {qresPath}";
            ClientDialogService.ShowError(errorOwner, "Error launching game", message);
            return null;
        }

        Process process;
        var startSw = Stopwatch.StartNew();
        try
        {
            process = StartGameProcess(startInfo);
            startSw.Stop();
            Logger.Log($"GameProcessLauncher: Process.Start returned in {startSw.ElapsedMilliseconds} ms (pid={process.Id}).");
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Logger.Log("Error launching QRes: " + ex);
            ClientDialogService.ShowError(
                errorOwner,
                "Error launching game",
                string.Format("Error launching {0}. Returned error: {1}", ProgramConstants.QRES_EXECUTABLE, ex.Message));
            return null;
        }

        if (Environment.ProcessorCount > 1 && SingleCoreAffinity)
            process.ProcessorAffinity = (IntPtr)2;

        return process;
    }

    private static string BuildSpawnArguments(string additionalExecutableName, string extraCommandLine)
    {
        if (string.IsNullOrWhiteSpace(extraCommandLine))
            return additionalExecutableName + "-SPAWN";

        return " " + additionalExecutableName + "-SPAWN " + extraCommandLine;
    }

    private static bool ValidateSyringeConfiguration(
        string launchExecutableName,
        string additionalExecutableName,
        string arguments,
        Window? errorOwner,
        out string message)
    {
        message = string.Empty;
        string gameExecutableName = AppState.Configuration.Legacy.GetGameExecutableName();

        if (IsSyringeLauncher(launchExecutableName) && string.IsNullOrWhiteSpace(additionalExecutableName))
        {
            message = "Syringe launch misconfigured: GameLauncherExecutableName is set but GameExecutableNames is missing.";
            ClientDialogService.ShowError(errorOwner, "Error launching game", message);
            return false;
        }

        if (IsSyringeLauncher(launchExecutableName)
            && !arguments.Contains(QuoteArgument(gameExecutableName), StringComparison.OrdinalIgnoreCase))
        {
            message = $"Syringe requires: Syringe.exe \"{gameExecutableName}\" -SPAWN … (got: {arguments.Trim()}).";
            ClientDialogService.ShowError(errorOwner, "Error launching game", message);
            return false;
        }

        return true;
    }

    private static Process StartGameProcess(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static bool WaitForIniPreprocessor(Window? errorOwner)
    {
        int waitTimes = 0;
        while (PreprocessorBackgroundTask.Instance.IsRunning)
        {
            Logger.Log("The preprocessor background task is still running. Wait for it...");
            Thread.Sleep(1000);
            waitTimes++;
            if (waitTimes <= 10)
                continue;

            ClientDialogService.ShowError(
                errorOwner,
                "INI preprocessing not complete",
                "INI preprocessing not complete. Please try launching the game again.");
            return false;
        }

        return true;
    }

    private static string QuoteArgument(string value) => "\"" + value + "\"";

    private static bool IsSyringeLauncher(string launchExecutableName)
        => launchExecutableName.Contains("syringe", StringComparison.OrdinalIgnoreCase);
}
