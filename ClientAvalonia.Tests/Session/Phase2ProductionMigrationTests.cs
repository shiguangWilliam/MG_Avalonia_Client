using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Phase 2 P2-1 / P2-3 / P2-5 生产迁移的新 API 单测。
///
/// 覆盖：
/// <list type="bullet">
/// <item><see cref="LobbyPlayerState.SyncFromSlots"/>：Session → LobbyState 投影</item>
/// <item><see cref="MultiplayerSlotCoordinator.HandleHostOptionsEdit(ICnCNetGameSession, string, IReadOnlyList{string})"/>
///   + <see cref="MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(ICnCNetGameSession, int)"/>：Session-aware 重载</item>
/// <item><see cref="LobbySessionState"/> ↔ <see cref="LobbyPlayerState"/> Owner 转发的"双份真相消除"</item>
/// </list>
/// </summary>
public sealed class Phase2ProductionMigrationTests
{
    private static CnCNetGameRoomSession NewHostSession(string localNick = "Alice")
    {
        var room = new CnCNetActiveGameRoom
        {
            RoomName = "Test",
            ChannelName = "#game-test",
            Password = "pw",
            Tunnel = new CnCNetTunnel { Name = "T", Address = "1.1.1.1", Port = 50000 },
            HostName = localNick,
            IsHost = true,
            MaxPlayers = 4,
        };
        return new CnCNetGameRoomSession(room);
    }

    [Fact]
    public void SyncFromSlots_Projects_Session_Slots_Into_LobbyState()
    {
        // Phase 2 P2-5：LobbyPlayerState.SyncFromSlots 是 Session → UI 绑定数组的投影。
        var state = new LobbyPlayerState();
        var session = new HostFakeSession();
        session.SetSlot(0, "Alice", isAi: false, isHumanLocal: true, side: 2, color: 3, team: 1, start: 4);
        session.SetSlot(1, "EasyAI", isAi: true, aiLevel: 0);

        state.SyncFromSlots(session.PlayerSlots);

        state.Slots[0].Name.Should().Be("Alice");
        state.Slots[0].SideIndex.Should().Be(2);
        state.Slots[0].ColorIndex.Should().Be(3);
        state.Slots[0].IsHumanLocal.Should().BeTrue();
        state.Slots[1].Name.Should().Be("EasyAI");
        state.Slots[1].IsAi.Should().BeTrue();
        state.Slots[1].AiLevel.Should().Be(0);
    }

    [Fact]
    public void SyncFromSlots_Null_Source_Throws()
    {
        var state = new LobbyPlayerState();
        Action act = () => state.SyncFromSlots(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SyncFromSlots_Truncates_And_Clears_When_Source_Shorter()
    {
        var state = new LobbyPlayerState();
        state.Slots[0].Name = "Stale";
        var session = new HostFakeSession(); // 默认所有槽位空

        state.SyncFromSlots(session.PlayerSlots);

        state.Slots[0].IsOccupied.Should().BeFalse("短源应清空目标槽位");
    }

    [Fact]
    public void LobbySessionState_OwnerBackref_Keeps_UIMode_Synchronized()
    {
        // Phase 2 P2-1：LobbySessionState ↔ LobbyPlayerState 双向转发，消除双份真相。
        var session = new LobbySessionState();

        session.UIMode = LobbyPlayerMode.Multiplayer;
        session.PlayerState.Mode.Should().Be(LobbyPlayerMode.Multiplayer, "写 owner 应转发到 PlayerState");

        session.PlayerState.Mode = LobbyPlayerMode.Skirmish;
        session.UIMode.Should().Be(LobbyPlayerMode.Skirmish, "写 PlayerState.Mode 应转发回 owner");
    }

    [Fact]
    public void LobbySessionState_OwnerBackref_Keeps_AllowHostPlayerOptions_Synchronized()
    {
        var session = new LobbySessionState();

        session.AllowHostPlayerOptions = false;
        session.PlayerState.AllowHostPlayerOptions.Should().BeFalse();

        session.PlayerState.AllowHostPlayerOptions = true;
        session.AllowHostPlayerOptions.Should().BeTrue();
    }

    [Fact]
    public void LobbySessionState_OwnerBackref_Keeps_LocalPlayerName_Synchronized()
    {
        var session = new LobbySessionState();

        session.LocalPlayerName = "Host";
        session.PlayerState.LocalPlayerName.Should().Be("Host");

        session.PlayerState.LocalPlayerName = "Joiner";
        session.LocalPlayerName.Should().Be("Joiner");
    }

    [Fact]
    public void LobbySessionState_Standalone_LobbyPlayerState_Still_Works()
    {
        // Owner == null（独立 new 的 LobbyPlayerState，比如老测试）应仍按本地字段存储。
        var standalone = new LobbyPlayerState();
        standalone.Mode = LobbyPlayerMode.Multiplayer;
        standalone.Mode.Should().Be(LobbyPlayerMode.Multiplayer);
        standalone.LocalPlayerName = "X";
        standalone.LocalPlayerName.Should().Be("X");
    }

    [Fact]
    public void HandleHostOptionsEdit_SessionOverload_Broadcasts_FromSlots()
    {
        // Phase 2 P2-3：Session-aware 重载走 BroadcastPlayerOptionsFromSlots，不再依赖 LobbyPlayerState。
        var session = NewHostSession("Alice");
        session.InitHostSlots("Alice");
        // 房主改自己 side=2
        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { SideIndex = 2, ColorIndex = 1 });

        // 因为 BroadcastPlayerOptionsFromSlots 需要 _connection 才能 SendCtcp，本测只验证 Revision bump。
        long before = session.Revision;
        MultiplayerSlotCoordinator.HandleHostOptionsEdit(session, "Alice", new[] { "Easy", "Medium" });
        session.Revision.Should().BeGreaterThan(before, "广播后必须 BumpRevision");
    }

    [Fact]
    public void HandleHostOptionsEdit_SessionOverload_NullSession_Throws()
    {
        Action act = () => MultiplayerSlotCoordinator.HandleHostOptionsEdit(
            null!, "Alice", Array.Empty<string>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HandleJoinerOptionsEdit_SessionOverload_NullSession_Throws()
    {
        Action act = () => MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(null!, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HandleJoinerOptionsEdit_SessionOverload_OutOfRangeSlot_Noops()
    {
        var session = NewHostSession("Alice");
        // 越界 index 不应抛
        var act = () => MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(session, -1);
        act.Should().NotThrow();
        var act2 = () => MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(session, 999);
        act2.Should().NotThrow();
    }

    [Fact]
    public void HandleJoinerOptionsEdit_SessionOverload_NonLocalSlot_Noops_Silently()
    {
        var session = NewHostSession("Alice");
        session.InitHostSlots("Alice");
        // slot 0 是本地人；但 joiner 路径本不该被 host 触发，验证不会异常
        long before = session.Revision;
        MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(session, 0);
        // RequestLocalPlayerOptions 在 IsHost=true 时直接 return，所以 Revision 不变
        session.Revision.Should().Be(before);
    }

    [Fact]
    public void SkirmishLaunchValidator_SessionOverload_Accepts_IPlayerSlot_List()
    {
        // Phase 2 P2-4：SkirmishLaunchValidator 新重载吃 IReadOnlyList<IPlayerSlot>。
        var map = MakeMap(maxPlayers: 4, minPlayers: 1);
        var gameMode = MakeGameMode();

        var slots = new List<IPlayerSlot>
        {
            new LobbyPlayerSlot { Name = "Alice", SideIndex = 0 },
            new LobbyPlayerSlot { Name = "EasyAI", IsAi = true, SideIndex = 0 },
        };

        string? result = SkirmishLaunchValidator.Validate(map, gameMode, slots, sideCount: 3);
        result.Should().BeNull("standard config should pass");
    }

    [Fact]
    public void SkirmishLaunchValidator_Legacy_Overload_Still_Works()
    {
        // 旧重载应仍工作（委托到新重载）。minPlayers=0 让空 lobby 通过。
        var map = MakeMap(maxPlayers: 4, minPlayers: 0, enforceMaxPlayers: true);
        var gameMode = MakeGameMode();
        var players = new LobbyPlayerState();

        string? result = SkirmishLaunchValidator.Validate(map, gameMode, players);
        result.Should().BeNull("empty lobby with minPlayers=0 passes");
    }

    [Fact]
    public void SkirmishLaunchValidator_SessionOverload_Detects_Over_Max_Players()
    {
        var map = MakeMap(maxPlayers: 2, enforceMaxPlayers: true);
        var gameMode = MakeGameMode();

        var slots = new List<IPlayerSlot>
        {
            new LobbyPlayerSlot { Name = "A", SideIndex = 0 },
            new LobbyPlayerSlot { Name = "B", SideIndex = 0 },
            new LobbyPlayerSlot { Name = "C", SideIndex = 0 },
        };

        string? result = SkirmishLaunchValidator.Validate(map, gameMode, slots, sideCount: 5);
        result.Should().NotBeNull();
        result.Should().Contain("more than 2");
    }

    private static MapEntry MakeMap(int maxPlayers = 4, int minPlayers = 0, bool enforceMaxPlayers = false)
        => new()
        {
            BaseFilePath = "test.map",
            DisplayName = "Test Map",
            UntranslatedName = "test.map",
            GameModes = new[] { "TestMode" },
            MaxPlayers = maxPlayers,
            MinPlayers = minPlayers,
            EnforceMaxPlayers = enforceMaxPlayers,
        };

    private static GameModeEntry MakeGameMode(string name = "TestMode")
        => new()
        {
            Name = name,
            DisplayName = name,
            UntranslatedUIName = name,
        };

    /// <summary>
    /// 极简 IGameSession 实现，仅供 SyncFromSlots 单测使用。
    /// 不实现网络协议；只暴露槽位数组。
    /// </summary>
    private sealed class HostFakeSession : IGameSession
    {
        private readonly LobbyPlayerSlot[] _slots =
            Enumerable.Range(0, LobbyPlayerSlot.MaxSlots).Select(_ => new LobbyPlayerSlot()).ToArray();
        private long _revision;

        public LobbyPlayerMode Mode => LobbyPlayerMode.Multiplayer;
        public long Revision => _revision;
        public IMapResource? Map { get; set; }
        public IReadOnlyList<IPlayerSlot> PlayerSlots => _slots;
        IGameOptionsState IGameSession.Options => throw new NotImplementedException();
        public GameSessionState State { get; set; } = GameSessionState.Lobby;
        public event Action? StateChanged { add { } remove { } }
        public IPlayerSlotSink SlotSink => throw new NotImplementedException();

        public void ResetSlotsForMap(int maxPlayers) => throw new NotImplementedException();

        internal void SetSlot(int idx, string name, bool isAi = false, bool isHumanLocal = false,
            int side = 0, int color = 0, int team = 0, int start = 0, int aiLevel = 0)
        {
            _slots[idx].Name = name;
            _slots[idx].IsAi = isAi;
            _slots[idx].IsHumanLocal = isHumanLocal;
            _slots[idx].SideIndex = side;
            _slots[idx].ColorIndex = color;
            _slots[idx].TeamIndex = team;
            _slots[idx].StartIndex = start;
            _slots[idx].AiLevel = aiLevel;
        }
    }
}
