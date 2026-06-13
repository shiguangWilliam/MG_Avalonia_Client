using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using ClientAvalonia.Core;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Platform;
using ClientCore;
using ClientCore.Extensions;
using ClientAvalonia.CnCNet;
using ClientCore.INIProcessing;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Launches the mod game executable via ClientCore configuration (aligned with ClientGUI GameProcessLogic).</summary>
public sealed class GameLaunchService
{
    private Process? _runningProcess;

    public bool IsRunning => _runningProcess is { HasExited: false };

    public event Action<string>? StatusChanged;

    /// <summary>Raised after the game process starts (UI should minimize / show in-progress overlay).</summary>
    public event Action? GameProcessStarted;

    /// <summary>Raised after the game process exits; client must stay open (XNA GameInProgressWindow).</summary>
    public event Action? GameProcessExited;

    public bool TryLaunchSkirmish(
        ClientEnvironment environment,
        SkirmishLaunchRequest request,
        out string message,
        Window? errorOwner = null)
    {
        SkirmishSpawnWriter.Write(request.Map, request.GameMode, request.Players, request.LobbyRoot);
        return LaunchGameProcess(environment, errorOwner, out message);
    }

    public bool TryLaunchCampaign(
        ClientEnvironment environment,
        CampaignLaunchRequest request,
        out string message,
        Window? errorOwner = null)
    {
        if (request.Mission.IsHeader || string.IsNullOrWhiteSpace(request.Mission.Scenario))
        {
            message = "No playable mission selected.";
            return false;
        }

        if (!request.Mission.Enabled)
        {
            message = "Selected mission is disabled.";
            return false;
        }

        CampaignSpawnWriter.Write(request.Mission, request.DifficultyIndex, request.OverlayRoot);
        return LaunchGameProcess(environment, errorOwner, out message);
    }

    public bool TryLaunchSkirmish(ClientEnvironment environment, out string message, Window? errorOwner = null)
        => LaunchGameProcess(environment, errorOwner, out message);

    public bool TryLaunchCnCNet(
        ClientEnvironment environment,
        CnCNetStartGameInfo startInfo,
        SkirmishLaunchRequest request,
        out string message,
        Window? errorOwner = null,
        IReadOnlyList<CnCNetGameRoomPlayer>? roomPlayers = null,
        CnCNetGameOptionsState? gameOptions = null)
    {
        CnCNetMultiplayerSpawnWriter.Write(
            request.Map,
            request.GameMode,
            startInfo,
            request.Players,
            request.LobbyRoot,
            roomPlayers,
            gameOptions);
        return LaunchGameProcess(environment, errorOwner, out message);
    }

    private bool LaunchGameProcess(ClientEnvironment environment, Window? errorOwner, out string message)
    {
        if (!ClientCoreBootstrap.IsInitialized)
            ClientCoreBootstrap.EnsureInitialized(environment.GameRoot);

        if (IsRunning)
        {
            message = "Game is already running.";
            return false;
        }

        if (!WaitForIniPreprocessor(errorOwner))
        {
            message = "INI preprocessing not complete.";
            return false;
        }

        OSVersion osVersion = ClientConfiguration.Instance.GetOperatingSystemVersion();
        string gameExecutableName = ClientConfiguration.Instance.GetGameExecutableName();
        if (string.IsNullOrWhiteSpace(gameExecutableName))
        {
            message = "Game executable is not configured (GameExecutableNames in ClientDefinitions.ini).";
            ClientDialogService.ShowError(errorOwner, "Error launching game", message);
            return false;
        }

        string launcherExecutableName = string.Empty;
        if (osVersion != OSVersion.UNIX)
            launcherExecutableName = ClientConfiguration.Instance.GameLauncherExecutableName?.Trim() ?? string.Empty;

        string launchExecutableName;
        string additionalExecutableName = string.Empty;

        if (osVersion == OSVersion.UNIX)
        {
            launchExecutableName = ClientConfiguration.Instance.UnixGameExecutableName;
            if (string.IsNullOrWhiteSpace(launchExecutableName))
                launchExecutableName = gameExecutableName;
        }
        else if (string.IsNullOrWhiteSpace(launcherExecutableName))
        {
            launchExecutableName = gameExecutableName;
        }
        else
        {
            launchExecutableName = launcherExecutableName;
            additionalExecutableName = QuoteArgument(gameExecutableName) + " ";
        }

        string extraCommandLine = ClientConfiguration.Instance.ExtraExeCommandLineParameters?.Trim() ?? string.Empty;

        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "DTA.LOG");
        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "TI.LOG");
        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "TS.LOG");

        string arguments;
        if (string.IsNullOrWhiteSpace(extraCommandLine))
            arguments = additionalExecutableName + "-SPAWN";
        else
            arguments = " " + additionalExecutableName + "-SPAWN " + extraCommandLine;

        if (!string.IsNullOrWhiteSpace(launcherExecutableName)
            && string.IsNullOrWhiteSpace(additionalExecutableName))
        {
            message = "Syringe launch misconfigured: GameLauncherExecutableName is set but GameExecutableNames is missing. "
                      + "Set GameExecutableNames=gamemd.exe and GameLauncherExecutableName=Syringe.exe in Resources/ClientDefinitions.ini.";
            ClientDialogService.ShowError(errorOwner, "Error launching game", message);
            return false;
        }

        if (IsSyringeLauncher(launchExecutableName)
            && !arguments.Contains(QuoteArgument(gameExecutableName), StringComparison.OrdinalIgnoreCase))
        {
            message = $"Syringe requires: Syringe.exe \"{gameExecutableName}\" -SPAWN … (got: {arguments.Trim()}). "
                      + "Check GameExecutableNames in ClientDefinitions.ini.";
            ClientDialogService.ShowError(errorOwner, "Error launching game", message);
            return false;
        }

        FileInfo gameFileInfo = SafePath.GetFile(ProgramConstants.GamePath, launchExecutableName);
        if (!gameFileInfo.Exists)
        {
            message = $"Launch executable not found: {gameFileInfo.FullName}";
            ClientDialogService.ShowError(errorOwner, "Error launching game", message);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = gameFileInfo.FullName,
                Arguments = arguments,
                WorkingDirectory = ProgramConstants.GamePath,
                UseShellExecute = false,
            };

            Logger.Log("Launch executable: " + startInfo.FileName);
            Logger.Log("Launch arguments: " + startInfo.Arguments);

            GameProcessStarting?.Invoke();

            Process process = StartDetachedProcess(startInfo);
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;

            _runningProcess = process;
            ProgramConstants.IsInGame = true;
            GameProcessStarted?.Invoke();

            message = $"Launched {launchExecutableName}";
            StatusChanged?.Invoke(message);
            return true;
        }
        catch (Exception ex)
        {
            string title = "Error launching game";
            string text = $"Error launching {gameFileInfo.Name}. {ex.Message}";
            ClientDialogService.ShowError(errorOwner, title, text);
            message = text;
            StatusChanged?.Invoke(message);
            return false;
        }
    }

    /// <summary>Aligned with XNA <see cref="ClientGUI.GameProcessLogic.GameProcessStarting"/>.</summary>
    public event Action? GameProcessStarting;

    private static Process StartDetachedProcess(ProcessStartInfo startInfo)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsDetachedProcess.Start(startInfo);

        var process = new Process { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is Process process)
        {
            process.Exited -= OnProcessExited;
            try
            {
                process.Dispose();
            }
            catch
            {
            }
        }

        _runningProcess = null;
        ProgramConstants.IsInGame = false;

        Logger.Log("GameLaunchService: game process exited.");
        GameProcessExited?.Invoke();
        StatusChanged?.Invoke("Game exited.");
    }

    private static bool WaitForIniPreprocessor(Window? errorOwner)
    {
        int waitTimes = 0;
        while (PreprocessorBackgroundTask.Instance.IsRunning)
        {
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
