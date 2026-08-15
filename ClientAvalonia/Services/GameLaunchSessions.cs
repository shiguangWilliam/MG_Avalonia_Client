using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Session;

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
    {
        IReadOnlyList<IPlayerSlot> slots = request.Slots ?? Array.Empty<LobbyPlayerSlot>();
        SkirmishSpawnWriter.Write(request.Map, request.GameMode, slots, request.SideCount, request.LobbyRoot);
    }
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
    private readonly LanStartGameInfo? _lan;
    private readonly IReadOnlyList<CnCNetGameRoomPlayer>? _roomPlayers;
    private readonly CnCNetGameOptionsState? _gameOptions;

    public MultiplayerLaunchSession(
        SkirmishLaunchRequest skirmish,
        CnCNetStartGameInfo? cncNet = null,
        IReadOnlyList<CnCNetGameRoomPlayer>? roomPlayers = null,
        CnCNetGameOptionsState? gameOptions = null,
        LanStartGameInfo? lan = null)
    {
        _skirmish = skirmish;
        _cncNet = cncNet;
        _roomPlayers = roomPlayers;
        _gameOptions = gameOptions;
        _lan = lan;
    }

    public string LaunchModeLabel
        => _cncNet != null ? "CnCNetMultiplayer"
            : _lan != null ? "LanMultiplayer"
            : "Multiplayer";

    public void PrepareSpawnFiles()
    {
        IReadOnlyList<IPlayerSlot> slots = _skirmish.Slots ?? Array.Empty<LobbyPlayerSlot>();

        if (_cncNet != null)
        {
            CnCNetMultiplayerSpawnWriter.Write(
                _skirmish.Map,
                _skirmish.GameMode,
                _cncNet,
                slots,
                _skirmish.LobbyRoot,
                _roomPlayers,
                _gameOptions);
            return;
        }

        if (_lan != null)
        {
            LanMultiplayerSpawnWriter.Write(
                _skirmish.Map,
                _skirmish.GameMode,
                _lan,
                slots,
                _skirmish.LobbyRoot,
                _skirmish.SideCount);
            return;
        }

        SkirmishSpawnWriter.Write(_skirmish.Map, _skirmish.GameMode, slots, _skirmish.SideCount, _skirmish.LobbyRoot);
    }
}
