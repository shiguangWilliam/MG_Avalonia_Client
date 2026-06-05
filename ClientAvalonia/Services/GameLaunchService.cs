using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using ClientAvalonia.Core;
using ClientAvalonia.IniUi.Loading;
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
        Window? errorOwner = null)
    {
        CnCNetMultiplayerSpawnWriter.Write(
            request.Map,
            request.GameMode,
            startInfo,
            request.Players,
            request.LobbyRoot);
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
        string gameExecutableName;
        string additionalExecutableName = string.Empty;

        if (osVersion == OSVersion.UNIX)
            gameExecutableName = ClientConfiguration.Instance.UnixGameExecutableName;
        else
        {
            string launcherExecutableName = ClientConfiguration.Instance.GameLauncherExecutableName;
            if (string.IsNullOrEmpty(launcherExecutableName))
                gameExecutableName = ClientConfiguration.Instance.GetGameExecutableName();
            else
            {
                gameExecutableName = launcherExecutableName;
                additionalExecutableName = "\"" + ClientConfiguration.Instance.GetGameExecutableName() + "\" ";
            }
        }

        string extraCommandLine = ClientConfiguration.Instance.ExtraExeCommandLineParameters;

        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "DTA.LOG");
        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "TI.LOG");
        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "TS.LOG");

        string arguments = string.IsNullOrWhiteSpace(extraCommandLine)
            ? additionalExecutableName + "-SPAWN"
            : " " + additionalExecutableName + "-SPAWN " + extraCommandLine;

        FileInfo gameFileInfo = SafePath.GetFile(ProgramConstants.GamePath, gameExecutableName);

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = gameFileInfo.FullName,
                    Arguments = arguments,
                    WorkingDirectory = ProgramConstants.GamePath,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true,
            };

            process.Exited += (_, _) =>
            {
                StatusChanged?.Invoke("Game exited.");
                _runningProcess = null;
            };

            Logger.Log("Launch executable: " + process.StartInfo.FileName);
            Logger.Log("Launch arguments: " + process.StartInfo.Arguments);

            process.Start();
            _runningProcess = process;
            message = $"Launched {gameExecutableName}";
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
}
