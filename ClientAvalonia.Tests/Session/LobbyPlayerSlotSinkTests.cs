using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// <see cref="LobbyPlayerSlotSink"/> 单测——见 docs/design/layered-architecture.md §2.2。
/// 覆盖：
/// <list type="bullet">
/// <item>OverwriteSlot / WriteSlot / ClearSlot / ClearAll / CopyFrom 全签名</item>
/// <item>静默版本不触发 onChanged，非静默触发</item>
/// <item>越界、空 update、null source 防御</item>
/// <item>CnCNet 字段（IsHost/Ready/Ping/Port）随写入生效</item>
/// </list>
/// </summary>
public sealed class LobbyPlayerSlotSinkTests
{
    private static LobbyPlayerSlot[] MakeSlots(int count = LobbyPlayerSlot.MaxSlots)
        => Enumerable.Range(0, count).Select(_ => new LobbyPlayerSlot()).ToArray();

    [Fact]
    public void WriteSlot_Sets_Specified_Field_Only()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        sink.WriteSlot(2, new SlotFieldUpdate { ColorIndex = 5, TeamIndex = 1 });

        slots[2].ColorIndex.Should().Be(5);
        slots[2].TeamIndex.Should().Be(1);
        slots[2].SideIndex.Should().Be(0, "未指定的字段保持原值");
        slots[2].Name.Should().BeEmpty();
        changes.Should().Be(1, "非静默写入触发 onChanged");
    }

    [Fact]
    public void WriteSlotSilent_Does_Not_Trigger_OnChanged()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        sink.WriteSlotSilent(0, new SlotFieldUpdate { SideIndex = 3 });

        slots[0].SideIndex.Should().Be(3);
        changes.Should().Be(0, "静默版本不触发 onChanged");
    }

    [Fact]
    public void WriteSlot_Empty_Update_Is_Noop()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        slots[1].Name = "Alice";
        slots[1].ColorIndex = 7;
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        sink.WriteSlot(1, default(SlotFieldUpdate));

        slots[1].Name.Should().Be("Alice");
        slots[1].ColorIndex.Should().Be(7);
        changes.Should().Be(0, "空 update 短路（Is_empty）");
    }

    [Fact]
    public void WriteSlot_Out_Of_Range_Is_Noop()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        Action act = () => sink.WriteSlot(-1, new SlotFieldUpdate { ColorIndex = 5 });
        Action act2 = () => sink.WriteSlot(slots.Length, new SlotFieldUpdate { ColorIndex = 5 });

        act.Should().NotThrow();
        act2.Should().NotThrow();
        changes.Should().Be(0);
    }

    [Fact]
    public void OverwriteSlot_Copies_All_Basic_Fields()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        var source = new LobbyPlayerSlot
        {
            Name = "Bob",
            SideIndex = 2,
            ColorIndex = 4,
            TeamIndex = 1,
            StartIndex = 3,
            AiLevel = 1,
            IsAi = false,
            IsHumanLocal = true,
        };

        sink.OverwriteSlot(5, source);

        slots[5].Name.Should().Be("Bob");
        slots[5].SideIndex.Should().Be(2);
        slots[5].ColorIndex.Should().Be(4);
        slots[5].TeamIndex.Should().Be(1);
        slots[5].StartIndex.Should().Be(3);
        slots[5].AiLevel.Should().Be(1);
        slots[5].IsHumanLocal.Should().BeTrue();
        changes.Should().Be(1);
    }

    [Fact]
    public void OverwriteSlot_Also_Copies_CnCNet_Fields_When_Target_Is_CnCNet()
    {
        // LobbyPlayerSlot 同时实现 ICnCNetPlayerSlot——所有 CnCNet 字段都该被覆盖
        LobbyPlayerSlot[] slots = MakeSlots();
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        var source = new LobbyPlayerSlot
        {
            Name = "CnCNetPlayer",
            IsHost = true,
            Ready = true,
            AutoReady = true,
            Ping = 42,
            Port = 12345,
        };

        sink.OverwriteSlot(0, source);

        slots[0].IsHost.Should().BeTrue();
        slots[0].Ready.Should().BeTrue();
        slots[0].AutoReady.Should().BeTrue();
        slots[0].Ping.Should().Be(42);
        slots[0].Port.Should().Be(12345);
    }

    [Fact]
    public void OverwriteSlotSilent_Does_Not_Trigger()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        sink.OverwriteSlotSilent(0, new LobbyPlayerSlot { Name = "X" });

        slots[0].Name.Should().Be("X");
        changes.Should().Be(0);
    }

    [Fact]
    public void ClearSlot_Resets_All_Fields()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        slots[3].Name = "Alice";
        slots[3].ColorIndex = 5;
        slots[3].SideIndex = 2;
        slots[3].IsHost = true;
        slots[3].Ping = 99;
        slots[3].Port = 5000;
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        sink.ClearSlot(3);

        slots[3].Name.Should().BeEmpty();
        slots[3].ColorIndex.Should().Be(0);
        slots[3].SideIndex.Should().Be(0);
        slots[3].IsHost.Should().BeFalse();
        slots[3].Ping.Should().Be(-1);
        slots[3].Port.Should().Be(0);
        changes.Should().Be(1);
    }

    [Fact]
    public void ClearSlot_Out_Of_Range_Is_Noop()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        Action act = () => sink.ClearSlot(-1);
        Action act2 = () => sink.ClearSlot(slots.Length);

        act.Should().NotThrow();
        act2.Should().NotThrow();
        changes.Should().Be(0);
    }

    [Fact]
    public void ClearAll_Resets_Every_Slot()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        foreach (LobbyPlayerSlot s in slots)
        {
            s.Name = "X";
            s.ColorIndex = 9;
            s.Ping = 100;
        }
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        sink.ClearAll();

        foreach (LobbyPlayerSlot s in slots)
        {
            s.Name.Should().BeEmpty();
            s.ColorIndex.Should().Be(0);
            s.Ping.Should().Be(-1);
        }
        changes.Should().Be(1, "ClearAll 仅触发一次 onChanged（批处理）");
    }

    [Fact]
    public void CopyFrom_Overwrites_And_Pads_With_Clear()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        slots[0].Name = "OldHuman";
        slots[0].ColorIndex = 7;
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        var source = new List<IPlayerSlot>
        {
            new LobbyPlayerSlot { Name = "NewA", ColorIndex = 1 },
            new LobbyPlayerSlot { Name = "NewB", ColorIndex = 2 },
        };

        sink.CopyFrom(source);

        slots[0].Name.Should().Be("NewA");
        slots[0].ColorIndex.Should().Be(1);
        slots[1].Name.Should().Be("NewB");
        slots[1].ColorIndex.Should().Be(2);
        slots[2].Name.Should().BeEmpty("原 slot 2 被清空");
        slots[2].ColorIndex.Should().Be(0);
        changes.Should().Be(1, "CopyAll 内部静默 + 末尾单次 onChanged");
    }

    [Fact]
    public void CopyFrom_Longer_Source_Truncates_Silently()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        var source = Enumerable.Range(0, slots.Length + 5)
            .Select(i => new LobbyPlayerSlot { Name = $"P{i}" })
            .ToList<IPlayerSlot>();

        sink.CopyFrom(source);

        slots[slots.Length - 1].Name.Should().Be($"P{slots.Length - 1}");
        changes.Should().Be(1);
    }

    [Fact]
    public void CnCNet_Field_Update_Works_On_LobbyPlayerSlot()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        int changes = 0;
        var sink = new LobbyPlayerSlotSink(() => slots, () => changes++);

        sink.WriteSlot(0, new SlotFieldUpdate
        {
            IsHost = true,
            Ready = true,
            Ping = 88,
            Port = 9999,
        });

        slots[0].IsHost.Should().BeTrue();
        slots[0].Ready.Should().BeTrue();
        slots[0].Ping.Should().Be(88);
        slots[0].Port.Should().Be(9999);
    }

    [Fact]
    public void SlotFieldUpdate_Options_Factory_Builds_Expected_Fields()
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

    [Fact]
    public void SlotFieldUpdate_IsEmpty_True_When_Nothing_Set()
    {
        default(SlotFieldUpdate).IsEmpty.Should().BeTrue();
        new SlotFieldUpdate { ColorIndex = 1 }.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Ctor_Null_Accessor_Throws()
    {
        // ReSharper disable once ObjectCreationAsStatement
        // 验证构造抛出，不需要保留实例
        Action act = () => { _ = new LobbyPlayerSlotSink(null!); };
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OverwriteSlot_Null_Source_Throws()
    {
        LobbyPlayerSlot[] slots = MakeSlots();
        var sink = new LobbyPlayerSlotSink(() => slots);

        Action act = () => sink.OverwriteSlot(0, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void No_OnChanged_Callback_Does_Not_Throw()
    {
        // 兼容场景：sink 不关心通知（纯写入）
        LobbyPlayerSlot[] slots = MakeSlots();
        var sink = new LobbyPlayerSlotSink(() => slots);

        Action act = () => sink.WriteSlot(0, new SlotFieldUpdate { ColorIndex = 1 });
        act.Should().NotThrow();
        slots[0].ColorIndex.Should().Be(1);
    }
}
