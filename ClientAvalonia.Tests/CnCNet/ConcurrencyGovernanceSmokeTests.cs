using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Session;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// 并发冒烟测试（并发治理方案 §5）：多线程读写共享集合与槽位，
/// 验证 ConcurrentDictionary 化与槽位锁 seam 不抛异常且终态一致。
/// </summary>
public sealed class ConcurrencyGovernanceSmokeTests
{
    private static CnCNetChatLine MakeLine(string sender)
        => new()
        {
            Scope = CnCNetChatScope.PrivateMessage,
            Sender = sender,
            DisplayText = $"[{sender}] hello",
        };

    [Fact]
    public async Task PrivateMessageThread_ConcurrentAppendAndRead_NoExceptionAndBounded()
    {
        var thread = new CnCNetPrivateMessageThread("peer");

        Task writer = Task.Run(() =>
        {
            for (int i = 0; i < 2_000; i++)
                thread.Append(MakeLine("peer"), incrementUnread: true);
        });

        Task reader = Task.Run(() =>
        {
            for (int i = 0; i < 2_000; i++)
            {
                IReadOnlyList<CnCNetChatLine> snapshot = thread.Messages;
                Assert.True(snapshot.Count <= 400);
            }
        });

        Task marker = Task.Run(() =>
        {
            for (int i = 0; i < 500; i++)
                thread.MarkRead();
        });

        await Task.WhenAll(writer, reader, marker);

        Assert.True(thread.Messages.Count <= 400);
        Assert.Equal(400, thread.Messages.Count);
    }

    [Fact]
    public void LobbyState_ConcurrentChatAndLog_AppendsAreBoundedAndReadable()
    {
        var state = new CnCNetLobbyState();

        Parallel.For(0, 4, _ =>
        {
            for (int i = 0; i < 500; i++)
            {
                state.AddChatLine(MakeLine("lobby"));
                state.AppendConnectionLog($"line {i}");
            }
        });

        Assert.True(state.ChatLines.Count <= 200);
        Assert.True(state.ConnectionLog.Count <= 80);
    }

    [Fact]
    public async Task SlotSink_WithSyncRoot_ConcurrentWrites_NoTornState()
    {
        // 模拟 CnCNet 双写者：UI sink 写 vs "IRC 读线程"写，共享同一锁根。
        object syncRoot = new();
        var slots = Enumerable.Range(0, LobbyPlayerSlot.MaxSlots)
            .Select(_ => new LobbyPlayerSlot())
            .ToArray();
        var sink = new LobbyPlayerSlotSink(() => slots, onChanged: null, syncRoot: syncRoot);

        var update = new SlotFieldUpdate
        {
            Name = "writer",
            SideIndex = 3,
            ColorIndex = 2,
            TeamIndex = 1,
            StartIndex = 4,
            IsAi = true,
        };

        Task uiWriter = Task.Run(() =>
        {
            for (int i = 0; i < 3_000; i++)
                sink.WriteSlot(i % LobbyPlayerSlot.MaxSlots, in update);
        });

        Task ircWriter = Task.Run(() =>
        {
            for (int i = 0; i < 3_000; i++)
            {
                lock (syncRoot)
                {
                    IPlayerSlot s = slots[i % LobbyPlayerSlot.MaxSlots];
                    s.SideIndex = 3;
                    s.ColorIndex = 2;
                    s.TeamIndex = 1;
                    s.StartIndex = 4;
                }
            }
        });

        await Task.WhenAll(uiWriter, ircWriter);

        foreach (IPlayerSlot s in slots)
        {
            Assert.Equal(3, s.SideIndex);
            Assert.Equal(2, s.ColorIndex);
            Assert.Equal(1, s.TeamIndex);
            Assert.Equal(4, s.StartIndex);
        }
    }

    [Fact]
    public void SlotSink_WithoutSyncRoot_SingleThreadStillWorks()
    {
        var slots = Enumerable.Range(0, LobbyPlayerSlot.MaxSlots)
            .Select(_ => new LobbyPlayerSlot())
            .ToArray();
        var sink = new LobbyPlayerSlotSink(() => slots);

        sink.WriteSlot(0, new SlotFieldUpdate { Name = "solo" });

        Assert.Equal("solo", slots[0].Name);
    }
}
