using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Core;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Loading;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>
/// Orchestrates spawn preparation + process launch for all game modes (DX <c>GameLobbyBase.StartGame</c>).
/// </summary>
public sealed class GameLaunchService
{
    private Process? _runningProcess;

    public bool IsRunning => _runningProcess is { HasExited: false };

    public event Action<string>? StatusChanged;
    public event Action? GameProcessStarting;
    public event Action? GameProcessStarted;
    public event Action? GameProcessExited;

    public bool UseQres
    {
        get => GameProcessLauncher.UseQres;
        set => GameProcessLauncher.UseQres = value;
    }

    public bool SingleCoreAffinity
    {
        get => GameProcessLauncher.SingleCoreAffinity;
        set => GameProcessLauncher.SingleCoreAffinity = value;
    }

    /// <summary>
    /// Lazily resolve <see cref="ICnCNetSession"/>. Returns null if no factory registered
    /// (e.g. during very early construction). The launch flow tolerates null.
    /// </summary>
    private static ICnCNetSession? ResolveCnCNet()
    {
        try { return EnvironmentServices.Resolve<ICnCNetSession>(); }
        catch (InvalidOperationException) { return null; }
    }

    public GameLaunchService()
    {
        GameProcessLauncher.GameProcessStarting += () =>
        {
            ProgramConstants.IsLaunchingGame = true;
            ResolveCnCNet()?.BeginLaunchPresenceKeepAlive();
            GameProcessStarting?.Invoke();
        };
        GameProcessLauncher.GameProcessStarted += () =>
        {
            ProgramConstants.IsInGame = true;
            ResolveCnCNet()?.NotifyGameProcessStarted();
            GameProcessStarted?.Invoke();
        };
        GameProcessLauncher.GameProcessExited += () =>
        {
            ProgramConstants.IsInGame = false;
            ProgramConstants.IsLaunchingGame = false;
            ResolveCnCNet()?.NotifyGameProcessExited();
            OnLauncherProcessExited();
        };
    }

    private void OnLauncherProcessExited()
    {
        _runningProcess = null;
        StatusChanged?.Invoke("Game exited.");
        GameProcessExited?.Invoke();
    }

    public bool TryLaunch(
        ClientEnvironment environment,
        IGameLaunchSession session,
        out string message,
        Window? errorOwner = null)
    {
        if (IsRunning)
        {
            message = "Game is already running.";
            return false;
        }

        string launchMode = session.LaunchModeLabel;
        var totalSw = Stopwatch.StartNew();
        GameLaunchDiagnostics.LogLaunchStart(launchMode, session);

        ProgramConstants.IsLaunchingGame = true;

        try
        {
            var spawnSw = Stopwatch.StartNew();
            Logger.Log($"GameLaunchService: preparing spawn files ({session.GetType().Name}, mode={launchMode}).");
            session.PrepareSpawnFiles();
            LogSpawnArtifacts();
            spawnSw.Stop();
            GameLaunchDiagnostics.LogAfterSpawnPrep(launchMode, spawnSw.ElapsedMilliseconds);

            var prepSw = Stopwatch.StartNew();
            if (!GameProcessLauncher.TryStart(environment, errorOwner, out Process? process, out message, out long prepMs)
                || process == null)
            {
                ProgramConstants.IsLaunchingGame = false;
                ResolveCnCNet()?.EndLaunchPresenceKeepAlive();
                GameLaunchDiagnostics.LogCnCNetState("launch-failed");
                return false;
            }

            prepSw.Stop();
            GameLaunchDiagnostics.LogAfterPreparation(launchMode, prepMs > 0 ? prepMs : prepSw.ElapsedMilliseconds);

            GameProcessLauncher.AttachExitHandler(process);
            _runningProcess = process;

            totalSw.Stop();
            GameLaunchDiagnostics.LogProcessStarted(launchMode, totalSw.ElapsedMilliseconds);

            StatusChanged?.Invoke(message);
            return true;
        }
        catch (Exception ex)
        {
            ProgramConstants.IsLaunchingGame = false;
            ResolveCnCNet()?.EndLaunchPresenceKeepAlive();
            message = ex.Message;
            GameLaunchDiagnostics.LogCnCNetState("launch-exception");
            ClientDialogService.ShowError(errorOwner, "Error launching game", message);
            return false;
        }
    }

    /// <summary>Spawn + launch on a worker thread so IRC/tunnel keepalive is not blocked on the UI thread.</summary>
    public void BeginLaunch(
        ClientEnvironment environment,
        IGameLaunchSession session,
        Action<bool, string> completion)
    {
        long queuedAt = Stopwatch.GetTimestamp();
        Logger.Log($"GameLaunchService: BeginLaunch queued (mode={session.LaunchModeLabel}).");

        Task.Run(() =>
        {
            long workerStartedAt = Stopwatch.GetTimestamp();
            double queueMs = (workerStartedAt - queuedAt) * 1000.0 / Stopwatch.Frequency;
            Logger.Log($"GameLaunchService: worker started after {queueMs:F1} ms (mode={session.LaunchModeLabel}).");

            try
            {
                bool ok = TryLaunch(environment, session, out string message, errorOwner: null);
                completion(ok, message);
            }
            catch (Exception ex)
            {
                Logger.Log("GameLaunchService.BeginLaunch failed: " + ex);
                completion(false, ex.Message);
            }
        });
    }

    private static void LogSpawnArtifacts()
    {
        FileInfo spawnIni = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
        FileInfo spawnMap = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNMAP_INI);

        Logger.Log(spawnIni.Exists
            ? $"GameLaunchService: spawn.ini ready ({spawnIni.Length} bytes) at {spawnIni.FullName}"
            : $"GameLaunchService: spawn.ini MISSING at {spawnIni.FullName}");

        Logger.Log(spawnMap.Exists
            ? $"GameLaunchService: spawnmap.ini ready ({spawnMap.Length} bytes)"
            : "GameLaunchService: spawnmap.ini MISSING");
    }

    public bool TryLaunchSkirmish(
        ClientEnvironment environment,
        SkirmishLaunchRequest request,
        out string message,
        Window? errorOwner = null)
        => TryLaunch(environment, new SkirmishLaunchSession(request), out message, errorOwner);

    public bool TryLaunchCampaign(
        ClientEnvironment environment,
        CampaignLaunchRequest request,
        out string message,
        Window? errorOwner = null)
        => TryLaunch(environment, new CampaignLaunchSession(request), out message, errorOwner);

    public bool TryLaunchMultiplayer(
        ClientEnvironment environment,
        SkirmishLaunchRequest request,
        out string message,
        Window? errorOwner = null,
        CnCNetStartGameInfo? cncNet = null,
        IReadOnlyList<CnCNetGameRoomPlayer>? roomPlayers = null,
        CnCNetGameOptionsState? gameOptions = null)
        => TryLaunch(
            environment,
            new MultiplayerLaunchSession(request, cncNet, roomPlayers, gameOptions),
            out message,
            errorOwner);

    /// <summary>CnCNet join/host after START CTCP.</summary>
    public bool TryLaunchCnCNet(
        ClientEnvironment environment,
        CnCNetStartGameInfo startInfo,
        SkirmishLaunchRequest request,
        out string message,
        Window? errorOwner = null,
        IReadOnlyList<CnCNetGameRoomPlayer>? roomPlayers = null,
        CnCNetGameOptionsState? gameOptions = null)
        => TryLaunchMultiplayer(environment, request, out message, errorOwner, startInfo, roomPlayers, gameOptions);

    /// <summary>LAN host/join — same spawn+launch pipeline as CnCNet until LAN-specific spawn exists.</summary>
    public bool TryLaunchLan(
        ClientEnvironment environment,
        SkirmishLaunchRequest request,
        out string message,
        Window? errorOwner = null)
        => TryLaunchMultiplayer(environment, request, out message, errorOwner);
}
