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
/// Phase 2 / Phase 6：Session 槽位真相源 + Coordinator Session-aware API。
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
    public void Session_PlayerSlots_Are_Mutable_Source_Of_Truth()
    {
        var session = new HostFakeSession();
        session.SetSlot(0, "Alice", isAi: false, isHumanLocal: true, side: 2, color: 3, team: 1, start: 4);
        session.SetSlot(1, "EasyAI", isAi: true, aiLevel: 0);

        session.PlayerSlots[0].Name.Should().Be("Alice");
        session.PlayerSlots[0].SideIndex.Should().Be(2);
        session.PlayerSlots[0].ColorIndex.Should().Be(3);
        session.PlayerSlots[0].IsHumanLocal.Should().BeTrue();
        session.PlayerSlots[1].Name.Should().Be("EasyAI");
        session.PlayerSlots[1].IsAi.Should().BeTrue();
        session.PlayerSlots[1].AiLevel.Should().Be(0);
    }

    [Fact]
    public void SkirmishSession_Owns_Private_Slots()
    {
        var session = new SkirmishSession();
        session.Slots[0].Name = "Local";
        session.PlayerSlots[0].Name.Should().Be("Local");
        ReferenceEquals(session.Slots, session.PlayerSlots).Should().BeTrue();
    }

    [Fact]
    public void LobbySessionState_UIMode_RoundTrips()
    {
        var session = new LobbySessionState();
        session.UIMode = LobbyPlayerMode.Multiplayer;
        session.UIMode.Should().Be(LobbyPlayerMode.Multiplayer);
        session.UIMode = LobbyPlayerMode.Skirmish;
        session.UIMode.Should().Be(LobbyPlayerMode.Skirmish);
    }

    [Fact]
    public void LobbySessionState_AllowHostPlayerOptions_RoundTrips()
    {
        var session = new LobbySessionState { AllowHostPlayerOptions = false };
        session.AllowHostPlayerOptions.Should().BeFalse();
        session.AllowHostPlayerOptions = true;
        session.AllowHostPlayerOptions.Should().BeTrue();
    }

    [Fact]
    public void LobbySessionState_LocalPlayerName_RoundTrips()
    {
        var session = new LobbySessionState { LocalPlayerName = "Host" };
        session.LocalPlayerName.Should().Be("Host");
        session.LocalPlayerName = "Joiner";
        session.LocalPlayerName.Should().Be("Joiner");
    }

    [Fact]
    public void HandleHostOptionsEdit_SessionOverload_Broadcasts_FromSlots()
    {
        var session = NewHostSession("Alice");
        session.InitHostSlots("Alice");
        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { SideIndex = 2, ColorIndex = 1 });

        long before = session.Revision;
        MultiplayerSlotCoordinator.HandleHostOptionsEdit(session, "Alice", new[] { "Easy", "Medium" });
        session.Revision.Should().BeGreaterThan(before);
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
        Action act = () => MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(session, -1);
        act.Should().NotThrow();
        Action act2 = () => MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(session, 999);
        act2.Should().NotThrow();
    }

    [Fact]
    public void HandleJoinerOptionsEdit_SessionOverload_NonLocalSlot_Noops_Silently()
    {
        var session = NewHostSession("Alice");
        session.InitHostSlots("Alice");
        long before = session.Revision;
        MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(session, 0);
        session.Revision.Should().Be(before);
    }

    [Fact]
    public void SkirmishLaunchValidator_SessionOverload_Accepts_IPlayerSlot_List()
    {
        var map = MakeMap(maxPlayers: 4, minPlayers: 1);
        var gameMode = MakeGameMode();

        var slots = new List<IPlayerSlot>
        {
            new LobbyPlayerSlot { Name = "Alice", SideIndex = 0 },
            new LobbyPlayerSlot { Name = "EasyAI", IsAi = true, SideIndex = 0 },
        };

        string? result = SkirmishLaunchValidator.Validate(map, gameMode, slots, sideCount: 3);
        result.Should().BeNull();
    }

    [Fact]
    public void SkirmishLaunchValidator_Empty_Slots_With_MinPlayers_Zero_Passes()
    {
        var map = MakeMap(maxPlayers: 4, minPlayers: 0, enforceMaxPlayers: true);
        var gameMode = MakeGameMode();
        var slots = Enumerable.Range(0, LobbyPlayerSlot.MaxSlots)
            .Select(_ => (IPlayerSlot)new LobbyPlayerSlot()).ToList();

        string? result = SkirmishLaunchValidator.Validate(map, gameMode, slots, sideCount: 3);
        result.Should().BeNull();
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

        public void NotifyStateChanged()
        {
        }

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
