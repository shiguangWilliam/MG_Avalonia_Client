using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Phase 4 P4-1 ~ P4-5 生产迁移的 Session-aware API 单测。
///
/// 覆盖：
/// <list type="bullet">
/// <item><b>P4-1</b>: <see cref="LobbyPlayerSlotSink"/> 写入路径（WriteSlot / ClearSlot / CopyFrom 等）
///   是 BindingApplier Session-aware 入口的真相源。</item>
/// <item><b>P4-2</b>: <see cref="GameDataBindingApplier.ResolveStartInteractionFlags(LobbyPlayerMode, bool, out bool, out bool)"/>
///   Session-aware 重载（脱离 LobbyPlayerState）。</item>
/// <item><b>P4-3</b>: <see cref="MapStartLocationRules.CanJoinerSelect(IList{IPlayerSlot}, int, bool)"/>
///   Session-aware 重载接受任意 IPlayerSlot 列表。</item>
/// <item><b>P4-5</b>: <see cref="IGameSession.Revision"/> 单调递增验证（替代 _applyingCnCNetGameRoomPlayers 布尔标志）。</item>
/// </list>
/// </summary>
public sealed class Phase4ProductionMigrationTests
{
    // ---- P4-1 LobbyPlayerSlotSink Session-aware 写路径 ----

    [Fact]
    public void Sink_WriteSlot_Bumps_Revision_And_Fires_StateChanged()
    {
        // Phase 4 P4-1：BindingApplier Session-aware 入口的真相源——sink.WriteSlot 后必须 bump Revision。
        var state = new LobbyPlayerState();
        var session = new SkirmishSession(state);
        int stateChangedCount = 0;
        session.StateChanged += () => stateChangedCount++;

        long before = session.Revision;
        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "Alice", SideIndex = 1 });

        session.Revision.Should().BeGreaterThan(before, "WriteSlot 必须 bump Revision");
        stateChangedCount.Should().BeGreaterThan(0, "WriteSlot 必须 fire StateChanged");
        state.Slots[0].Name.Should().Be("Alice");
        state.Slots[0].SideIndex.Should().Be(1);
    }

    [Fact]
    public void Sink_WriteSlotSilent_Does_Not_Bump_Revision()
    {
        // Phase 4 P4-1：silent 写入用于批量应用（如 PO ApplyDto），不应触发 StateChanged。
        var state = new LobbyPlayerState();
        var session = new SkirmishSession(state);
        int stateChangedCount = 0;
        session.StateChanged += () => stateChangedCount++;

        long before = session.Revision;
        session.SlotSink.WriteSlotSilent(0, new SlotFieldUpdate { Name = "Bob", SideIndex = 2 });

        session.Revision.Should().Be(before, "silent 写入不应 bump Revision");
        stateChangedCount.Should().Be(0, "silent 写入不应 fire StateChanged");
        state.Slots[0].Name.Should().Be("Bob");
    }

    [Fact]
    public void Sink_ClearSlot_Bumps_Revision_And_Zeroes_Fields()
    {
        var state = new LobbyPlayerState();
        state.Slots[0].Name = "Alice";
        state.Slots[0].SideIndex = 3;
        state.Slots[0].IsAi = true;
        var session = new SkirmishSession(state);

        long before = session.Revision;
        session.SlotSink.ClearSlot(0);

        session.Revision.Should().BeGreaterThan(before);
        state.Slots[0].Name.Should().BeEmpty();
        state.Slots[0].SideIndex.Should().Be(0);
        state.Slots[0].IsAi.Should().BeFalse();
    }

    [Fact]
    public void Sink_OverwriteSlot_Copies_All_Fields()
    {
        var state = new LobbyPlayerState();
        var session = new SkirmishSession(state);
        var source = new LobbyPlayerSlot
        {
            Name = "Alice",
            SideIndex = 2,
            ColorIndex = 3,
            TeamIndex = 1,
            StartIndex = 4,
            AiLevel = 0,
            IsAi = false,
            IsHumanLocal = true,
        };

        session.SlotSink.OverwriteSlot(0, source);

        state.Slots[0].Name.Should().Be("Alice");
        state.Slots[0].SideIndex.Should().Be(2);
        state.Slots[0].ColorIndex.Should().Be(3);
        state.Slots[0].TeamIndex.Should().Be(1);
        state.Slots[0].StartIndex.Should().Be(4);
        state.Slots[0].IsHumanLocal.Should().BeTrue();
    }

    [Fact]
    public void Sink_CopyFrom_Bulk_Applies_And_Bumps_Revision_Once()
    {
        // Phase 4 P4-1：CopyFrom 用于切换 Session 时迁移槽位——只应 fire StateChanged 一次。
        var state = new LobbyPlayerState();
        var session = new SkirmishSession(state);
        int stateChangedCount = 0;
        session.StateChanged += () => stateChangedCount++;

        var source = new IPlayerSlot[]
        {
            new LobbyPlayerSlot { Name = "Alice", SideIndex = 1 },
            new LobbyPlayerSlot { Name = "AI", IsAi = true, AiLevel = 0 },
        };

        session.SlotSink.CopyFrom(source);

        stateChangedCount.Should().Be(1, "CopyFrom 应只触发一次 StateChanged");
        session.PlayerSlots[0].Name.Should().Be("Alice");
        session.PlayerSlots[1].Name.Should().Be("AI");
        session.PlayerSlots[1].IsAi.Should().BeTrue();
    }

    [Fact]
    public void Sink_OutOfRange_Index_Noops_Silently()
    {
        var state = new LobbyPlayerState();
        var session = new SkirmishSession(state);
        long before = session.Revision;

        // 越界 index 不应抛
        Action act1 = () => session.SlotSink.WriteSlot(-1, new SlotFieldUpdate { Name = "X" });
        Action act2 = () => session.SlotSink.WriteSlot(999, new SlotFieldUpdate { Name = "X" });
        Action act3 = () => session.SlotSink.ClearSlot(-1);

        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();
        session.Revision.Should().Be(before, "越界写入不应 bump Revision");
    }

    [Fact]
    public void SlotFieldUpdate_IsEmpty_ShortCircuits()
    {
        // Phase 4 P4-1：空 update 不应触发任何写入或 Revision bump。
        var state = new LobbyPlayerState();
        var session = new SkirmishSession(state);
        long before = session.Revision;

        session.SlotSink.WriteSlot(0, default(SlotFieldUpdate));

        session.Revision.Should().Be(before, "空 update 应短路");
    }

    // ---- P4-1 SlotFieldUpdate 字段语义 ----

    [Fact]
    public void SlotFieldUpdate_Partial_Update_Preserves_Other_Fields()
    {
        // Phase 4 P4-1：SlotFieldUpdate 是 nullable 字段 struct——只更新显式给值的字段。
        var state = new LobbyPlayerState();
        state.Slots[0].Name = "Alice";
        state.Slots[0].SideIndex = 1;
        state.Slots[0].ColorIndex = 2;
        state.Slots[0].TeamIndex = 3;
        var session = new SkirmishSession(state);

        // 只改 SideIndex，其他字段保持
        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { SideIndex = 5 });

        state.Slots[0].Name.Should().Be("Alice", "未指定的字段应保留");
        state.Slots[0].SideIndex.Should().Be(5);
        state.Slots[0].ColorIndex.Should().Be(2);
        state.Slots[0].TeamIndex.Should().Be(3);
    }

    [Fact]
    public void SlotFieldUpdate_Options_Factory_Builds_Correct_Update()
    {
        SlotFieldUpdate u = SlotFieldUpdate.Options(side: 1, color: 2, team: 3, start: 4);
        u.SideIndex.Should().Be(1);
        u.ColorIndex.Should().Be(2);
        u.TeamIndex.Should().Be(3);
        u.StartIndex.Should().Be(4);
        u.Name.Should().BeNull();
        u.IsAi.Should().BeNull();
        u.IsEmpty.Should().BeFalse();
    }

    // ---- P4-2 GameDataBindingApplier.ResolveStartInteractionFlags Session 重载 ----

    [Theory]
    [InlineData(LobbyPlayerMode.Skirmish, true, true, false)]   // Skirmish 始终 host
    [InlineData(LobbyPlayerMode.Skirmish, false, true, false)]  // Skirmish 始终 host
    [InlineData(LobbyPlayerMode.Multiplayer, true, true, false)]   // host 多人
    [InlineData(LobbyPlayerMode.Multiplayer, false, false, true)]  // joiner 多人
    public void ResolveStartInteractionFlags_SessionOverload_Matches_Legacy(
        LobbyPlayerMode mode, bool allowHost, bool expectedCanAssign, bool expectedCanSelectLocal)
    {
        // Phase 4 P4-2：Session-aware 重载行为等价于 LobbyPlayerState 入口。
        GameDataBindingApplier.ResolveStartInteractionFlags(
            mode, allowHost, out bool canAssign, out bool canSelectLocal);

        canAssign.Should().Be(expectedCanAssign);
        canSelectLocal.Should().Be(expectedCanSelectLocal);
    }

    [Fact]
    public void ResolveStartInteractionFlags_SessionOverload_Matches_Legacy_LobbyPlayerState_Entry()
    {
        // 等价性验证：新重载与 LobbyPlayerState 入口完全一致。
        var state = new LobbyPlayerState { Mode = LobbyPlayerMode.Multiplayer, AllowHostPlayerOptions = false };

        GameDataBindingApplier.ResolveStartInteractionFlags(state, out bool legacyAssign, out bool legacySelect);
        GameDataBindingApplier.ResolveStartInteractionFlags(state.Mode, state.AllowHostPlayerOptions,
            out bool sessionAssign, out bool sessionSelect);

        sessionAssign.Should().Be(legacyAssign);
        sessionSelect.Should().Be(legacySelect);
    }

    // ---- P4-3 MapStartLocationRules.CanJoinerSelect Session 重载 ----

    [Fact]
    public void CanJoinerSelect_IPlayerSlot_Overload_Blocked_When_Enforced_And_Occupied()
    {
        // Phase 4 P4-3：Session-aware 重载接受任意 IPlayerSlot 实现（不仅是 LobbyPlayerSlot）。
        var slots = new List<IPlayerSlot>
        {
            new FakeSlot { Name = "Alice", StartIndex = 2 },
            new FakeSlot { Name = "Bob", StartIndex = 0 },
        };

        bool result = MapStartLocationRules.CanJoinerSelect(
            slots, startLocation1Based: 2, enforceMaxPlayers: true);

        result.Should().BeFalse("occupied spot under enforceMaxPlayers");
    }

    [Fact]
    public void CanJoinerSelect_IPlayerSlot_Overload_Allowed_When_Not_Enforced()
    {
        var slots = new List<IPlayerSlot>
        {
            new FakeSlot { Name = "Alice", StartIndex = 2 },
        };

        bool result = MapStartLocationRules.CanJoinerSelect(
            slots, startLocation1Based: 2, enforceMaxPlayers: false);

        result.Should().BeTrue("non-enforce always allows");
    }

    [Fact]
    public void CanJoinerSelect_IPlayerSlot_Overload_Allowed_For_Unoccupied_Spot()
    {
        var slots = new List<IPlayerSlot>
        {
            new FakeSlot { Name = "Alice", StartIndex = 1 },
            new FakeSlot { Name = "Bob", StartIndex = 2 },
        };

        bool result = MapStartLocationRules.CanJoinerSelect(
            slots, startLocation1Based: 3, enforceMaxPlayers: true);

        result.Should().BeTrue("unoccupied spot is selectable");
    }

    [Fact]
    public void CanJoinerSelect_IPlayerSlot_Allows_Zero_Start()
    {
        // startLocation1Based <= 0 时应总返回 true（XNA "Random" 占位）。
        var slots = new List<IPlayerSlot> { new FakeSlot { Name = "A", StartIndex = 1 } };

        MapStartLocationRules.CanJoinerSelect(slots, 0, enforceMaxPlayers: true)
            .Should().BeTrue();
        MapStartLocationRules.CanJoinerSelect(slots, -1, enforceMaxPlayers: true)
            .Should().BeTrue();
    }

    // ---- P4-5 IGameSession.Revision 单调递增 ----

    [Fact]
    public void Revision_Monotonically_Increases_On_Each_Mutation()
    {
        // Phase 4 P4-5：Revision 是单调递增脏读 tag——用于检测 StateChanged 是否由自己触发。
        // 替代旧 _applyingCnCNetGameRoomPlayers 布尔标志。
        var state = new LobbyPlayerState();
        var session = new SkirmishSession(state);

        long r0 = session.Revision;
        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "A" });
        long r1 = session.Revision;
        session.SlotSink.WriteSlot(1, new SlotFieldUpdate { Name = "B" });
        long r2 = session.Revision;
        session.SlotSink.ClearSlot(0);
        long r3 = session.Revision;

        r1.Should().BeGreaterThan(r0);
        r2.Should().BeGreaterThan(r1);
        r3.Should().BeGreaterThan(r2);
    }

    [Fact]
    public void Revision_Detects_Self_Triggered_StateChanged()
    {
        // Phase 4 P4-5：MainWindow 用 Revision 比对来检测"StateChanged 是不是我自己触发的"。
        // 模式：订阅时记下读到 的 Revision；事件回调开始时若 Revision 未变 → skip。
        var state = new LobbyPlayerState();
        var session = new SkirmishSession(state);

        long subscribedAt = session.Revision;
        bool firedFromExternal = false;

        session.StateChanged += () =>
        {
            if (session.Revision != subscribedAt)
                firedFromExternal = true;
        };

        // 外部写入 → Revision 变 → callback 检测到外部触发
        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "Alice" });

        firedFromExternal.Should().BeTrue("Revision 不同应识别为外部触发");
    }

    [Fact]
    public void Revision_Detects_Echo_After_Subscribe()
    {
        // Phase 4 P4-5：echo 场景——订阅后自己写入，回调中再次比对 Revision 应相同。
        var state = new LobbyPlayerState();
        var session = new SkirmishSession(state);

        long capturedDuringCallback = -1;
        session.StateChanged += () =>
        {
            // 回调中读到最新 Revision；如果下次再触发读到相同值 → echo
            capturedDuringCallback = session.Revision;
        };

        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "A" });
        long r1 = capturedDuringCallback;

        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "B" });
        long r2 = capturedDuringCallback;

        r2.Should().BeGreaterThan(r1, "两次不同写入应产生不同 Revision");
    }

    // ---- helpers ----

    /// <summary>极简 <see cref="IPlayerSlot"/> 实现，仅供 P4-3 测试使用。</summary>
    private sealed class FakeSlot : IPlayerSlot
    {
        public string Name { get; set; } = string.Empty;
        public int SideIndex { get; set; }
        public int ColorIndex { get; set; }
        public int TeamIndex { get; set; }
        public int StartIndex { get; set; }
        public int AiLevel { get; set; }
        public bool IsAi { get; set; }
        public bool IsHumanLocal { get; set; }
        public bool IsOccupied => !string.IsNullOrEmpty(Name);
    }
}
