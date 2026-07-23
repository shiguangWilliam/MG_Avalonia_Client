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
        // Phase 3 P3-2：优先走 Session-aware 入口（Slots + SideCount）。
        IReadOnlyList<IPlayerSlot>? slots = request.Slots;
        if (slots == null)
        {
#pragma warning disable CS0618 // 兼容期：调用方未升级时退回 Players。
            LobbyPlayerState? legacy = request.Players;
            int sideCount = legacy?.SideNames.Count ?? 0;
            slots = legacy?.Slots ?? Array.Empty<LobbyPlayerSlot>();
            SkirmishSpawnWriter.Write(request.Map, request.GameMode, slots, sideCount, request.LobbyRoot);
            return;
#pragma warning restore CS0618
        }

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
        // Phase 3 P3-2：优先走 Session-aware 入口（Slots）；否则退回 Players（legacy）。
        IReadOnlyList<IPlayerSlot>? slots = _skirmish.Slots;
        bool useLegacyPlayers = slots == null;
        if (useLegacyPlayers)
        {
#pragma warning disable CS0618
            slots = _skirmish.Players?.Slots ?? Array.Empty<LobbyPlayerSlot>();
#pragma warning restore CS0618
        }

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

        // LAN / generic multiplayer until dedicated LAN spawn additions exist.
        SkirmishSpawnWriter.Write(_skirmish.Map, _skirmish.GameMode, slots, _skirmish.SideCount, _skirmish.LobbyRoot);
    }
}
