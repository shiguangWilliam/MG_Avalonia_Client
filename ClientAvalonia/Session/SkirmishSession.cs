using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.Services;

namespace ClientAvalonia.Session;

/// <summary>
/// 遭遇战会话默认实现。
///
/// 作用：包装现有 LobbyPlayerState，对外暴露 ISkirmishSession 契约。
/// </summary>
public sealed class SkirmishSession : ISkirmishSession
{
    private IMapResource? _map;
    private GameSessionState _state = GameSessionState.Lobby;
    private long _revision;

    public SkirmishSession(LobbyPlayerState? player = null)
    {
        Player = player ?? new LobbyPlayerState();
        SlotSink = new LobbyPlayerSlotSink(
            () => Player.Slots,
            () => BumpRevision());
    }

    private void BumpRevision()
    {
        _revision++;
        StateChanged?.Invoke();
    }

    /// <inheritdoc />
    public LobbyPlayerMode Mode => LobbyPlayerMode.Skirmish;

    /// <inheritdoc />
    public long Revision => _revision;

    /// <summary>底层玩家槽位状态（与现有 LobbyPlayerBindingApplier 兼容）。</summary>
    /// <remarks>
    /// 兼容期保留：现有 LobbyPlayerBindingApplier / MultiplayerSlotCoordinator 仍依赖此属性。
    /// Step 2 会把 LobbyPlayerState 降为 <see cref="PlayerSlots"/> 的投影。
    /// Phase 2 P2-6：标记为已过时——外部应通过 <see cref="PlayerSlots"/> / <see cref="SlotSink"/> 操作槽位。
    /// Phase 3 将真正私有化。
    /// </remarks>
    [Obsolete("Phase 2 P2-6: 外部应通过 PlayerSlots / SlotSink 操作。Phase 4 完成 BindingApplier Session-aware 路径；Phase 5 私有化。")]
    public LobbyPlayerState Player { get; }

    /// <summary>可变游戏选项。</summary>
    public GameOptionsState Options { get; } = new();

    /// <inheritdoc />
    public IPlayerSlotSink SlotSink { get; }

    /// <inheritdoc />
    public void ResetSlotsForMap(int maxPlayers)
    {
        try
        {
            Domain.Resources.IMultiplayerColorCatalog colors =
                GlobalState.Environment.EnvironmentServices.Resolve<Domain.Resources.IMultiplayerColorCatalog>();
            string playerName = GlobalState.Environment.EnvironmentServices
                .Resolve<GlobalState.Environment.IGameEnvironment>().PlayerName;
            IniUi.Lobby.DefaultAiSlotPolicy.AutoFillToMapCapacity(
                this, maxPlayers, playerName, colors, Player.AiNames);
        }
        catch (InvalidOperationException)
        {
            Player.LoadDefaultSkirmishSlots(maxPlayers);
        }
        BumpRevision();
    }

    /// <inheritdoc />
    public IMapResource? Map
    {
        get => _map;
        set
        {
            _map = value;
            BumpRevision();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 当前直接返回 <see cref="Player"/>.<see cref="LobbyPlayerState.Slots"/>（LobbyPlayerSlot[] 实现了 IPlayerSlot[]）。
    /// Step 2 后会改为本类私有存储 + LobbyPlayerState 投影它。
    /// </remarks>
    public IReadOnlyList<IPlayerSlot> PlayerSlots => Player.Slots;

    /// <inheritdoc />
    IGameOptionsState IGameSession.Options => Options;

    /// <inheritdoc />
    public GameSessionState State
    {
        get => _state;
        set
        {
            if (_state == value)
                return;

            _state = value;
            BumpRevision();
        }
    }

    /// <inheritdoc />
    public event Action? StateChanged;

    /// <summary>通知 UI 刷新（槽位 / 选项变更后调用）。</summary>
    public void NotifyStateChanged() => StateChanged?.Invoke();
}

