using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;

namespace ClientAvalonia.Services;

/// <summary>
/// Prepares spawn files before <see cref="GameProcessLauncher"/> starts the game process.
/// Skirmish, CnCNet, LAN, and campaign modes each supply one implementation — no duplicate launchers.
/// </summary>
public interface IGameLaunchSession
{
    string LaunchModeLabel { get; }

    void PrepareSpawnFiles();
}

public sealed class SkirmishLaunchSession(SkirmishLaunchRequest request) : IGameLaunchSession
{
    public string LaunchModeLabel => "Skirmish";

    public void PrepareSpawnFiles()
        => SkirmishSpawnWriter.Write(request.Map, request.GameMode, request.Players, request.LobbyRoot);
}

public sealed class CampaignLaunchSession(CampaignLaunchRequest request) : IGameLaunchSession
{
    public string LaunchModeLabel => "Campaign";

    public void PrepareSpawnFiles()
        => CampaignSpawnWriter.Write(request.Mission, request.DifficultyIndex, request.OverlayRoot);
}

/// <summary>Shared by CnCNet and LAN multiplayer (DX <c>GameLobbyBase.StartGame</c> spawn phase).</summary>
public sealed class MultiplayerLaunchSession : IGameLaunchSession
{
    private readonly SkirmishLaunchRequest _skirmish;
    private readonly CnCNetStartGameInfo? _cncNet;
    private readonly IReadOnlyList<CnCNetGameRoomPlayer>? _roomPlayers;
    private readonly CnCNetGameOptionsState? _gameOptions;

    public MultiplayerLaunchSession(
        SkirmishLaunchRequest skirmish,
        CnCNetStartGameInfo? cncNet = null,
        IReadOnlyList<CnCNetGameRoomPlayer>? roomPlayers = null,
        CnCNetGameOptionsState? gameOptions = null)
    {
        _skirmish = skirmish;
        _cncNet = cncNet;
        _roomPlayers = roomPlayers;
        _gameOptions = gameOptions;
    }

    public string LaunchModeLabel => _cncNet != null ? "CnCNetMultiplayer" : "LanMultiplayer";

    public void PrepareSpawnFiles()
    {
        if (_cncNet != null)
        {
            CnCNetMultiplayerSpawnWriter.Write(
                _skirmish.Map,
                _skirmish.GameMode,
                _cncNet,
                _skirmish.Players,
                _skirmish.LobbyRoot,
                _roomPlayers,
                _gameOptions);
            return;
        }

        // LAN / generic multiplayer until dedicated LAN spawn additions exist.
        SkirmishSpawnWriter.Write(_skirmish.Map, _skirmish.GameMode, _skirmish.Players, _skirmish.LobbyRoot);
    }
}
