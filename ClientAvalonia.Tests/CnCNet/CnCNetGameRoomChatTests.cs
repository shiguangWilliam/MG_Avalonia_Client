using System;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Unit tests for the in-room chat timeline on <see cref="CnCNetGameRoomSession"/>.
///
/// Coverage philosophy (DX-aligned): DX's <c>Channel</c> model maintains a single message
/// list fed by three sources — own sends, remote PRIVMSG, and AddNotice. Our room session
/// mirrors that with <see cref="CnCNetGameRoomSession.AppendLocalChat"/>,
/// <see cref="CnCNetGameRoomSession.AppendRemoteChat"/>, and
/// <see cref="CnCNetGameRoomSession.AddRoomNotice"/>. These tests pin that contract.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class CnCNetGameRoomChatTests : IDisposable
{
    private readonly TempGameRootFixture _fixture = new();

    public CnCNetGameRoomChatTests()
    {
        // ProgramConstants.PLAYERNAME is read by the room session for local nick echo.
        // Pin it so AppendLocalChat attribution is deterministic.
        ProgramConstants.PLAYERNAME = "LocalPlayer";
    }

    public void Dispose()
    {
        ProgramConstants.PLAYERNAME = "Player";
        _fixture.Dispose();
    }

    [Fact]
    public void ChatLines_IsEmpty_ForFreshRoom()
    {
        CnCNetGameRoomSession room = MakeRoom();

        room.ChatLines.Should().BeEmpty();
    }

    [Fact]
    public void AppendRemoteChat_AddsToTimeline_WithGameRoomScope()
    {
        CnCNetGameRoomSession room = MakeRoom();

        room.AppendRemoteChat("Alice", "[10:00] Alice: hi");

        room.ChatLines.Should().HaveCount(1);
        CnCNetChatLine line = room.ChatLines[0];
        line.Sender.Should().Be("Alice");
        line.DisplayText.Should().Be("[10:00] Alice: hi");
        line.Scope.Should().Be(CnCNetChatScope.GameRoom);
        line.IsSystem.Should().BeFalse();
        line.TextColor.Should().Be(Avalonia.Media.Colors.White);
    }

    [Fact]
    public void AppendLocalChat_AttributesToLocalNick_WithGameRoomScope()
    {
        CnCNetGameRoomSession room = MakeRoom();

        room.AppendLocalChat("[10:01] LocalPlayer: yo", Avalonia.Media.Colors.Orange);

        room.ChatLines.Should().HaveCount(1);
        CnCNetChatLine line = room.ChatLines[0];
        line.Sender.Should().Be("LocalPlayer");
        line.DisplayText.Should().Be("[10:01] LocalPlayer: yo");
        line.Scope.Should().Be(CnCNetChatScope.GameRoom);
        line.TextColor.Should().Be(Avalonia.Media.Colors.Orange);
    }

    [Fact]
    public void AddRoomNotice_MarksLineAsSystem_WithGameRoomScope()
    {
        CnCNetGameRoomSession room = MakeRoom();

        room.AddRoomNotice("Alice has joined the game.");

        room.ChatLines.Should().HaveCount(1);
        CnCNetChatLine line = room.ChatLines[0];
        line.IsSystem.Should().BeTrue();
        line.Sender.Should().BeEmpty();
        line.DisplayText.Should().Be("Alice has joined the game.");
        line.Scope.Should().Be(CnCNetChatScope.GameRoom);
        line.TextColor.Should().Be(CnCNetIrcChatText.SystemNoticeColor);
    }

    [Fact]
    public void AppendRemoteChat_BlankText_IsIgnored()
    {
        CnCNetGameRoomSession room = MakeRoom();

        room.AppendRemoteChat("Alice", "   ");
        room.AppendRemoteChat("Alice", "");

        room.ChatLines.Should().BeEmpty();
    }

    [Fact]
    public void AddRoomNotice_BlankText_IsIgnored()
    {
        CnCNetGameRoomSession room = MakeRoom();

        room.AddRoomNotice("   ");
        room.AddRoomNotice(string.Empty);

        room.ChatLines.Should().BeEmpty();
    }

    [Fact]
    public void ClearChat_RemovesAllLines()
    {
        CnCNetGameRoomSession room = MakeRoom();
        room.AppendRemoteChat("Alice", "hi");
        room.AppendLocalChat("yo");

        room.ChatLines.Should().HaveCount(2);

        room.ClearChat();

        room.ChatLines.Should().BeEmpty();
    }

    [Fact]
    public void AppendRemoteChat_TrimsWithFifoCap_At200Lines()
    {
        CnCNetGameRoomSession room = MakeRoom();

        for (int i = 0; i < 250; i++)
            room.AppendRemoteChat("Alice", $"msg {i}");

        room.ChatLines.Should().HaveCount(200);
        // Oldest 50 should have been dropped; first remaining line is msg 50.
        room.ChatLines[0].DisplayText.Should().Be("msg 50");
        room.ChatLines[^1].DisplayText.Should().Be("msg 249");
    }

    [Fact]
    public void ChatChanged_EventFires_OnAppend()
    {
        CnCNetGameRoomSession room = MakeRoom();
        int fires = 0;
        room.ChatChanged += () => fires++;

        room.AppendRemoteChat("Alice", "hi");
        room.AppendLocalChat("yo");
        room.AddRoomNotice("notice");
        room.ClearChat();

        fires.Should().Be(4);
    }

    [Fact]
    public void TrySendChat_Rejects_WhenNoConnectionAttached()
    {
        // Without calling Attach(), the room has no IRC connection, so TrySendChat must
        // refuse to send even if the message is valid.
        CnCNetGameRoomSession room = MakeRoom();

        CnCNetGameRoomSession.RoomChatSendResult result = room.TrySendChat("hello", ircColorId: 5);

        result.Should().Be(CnCNetGameRoomSession.RoomChatSendResult.Failed);
        room.ChatLines.Should().BeEmpty("TrySendChat only queues IRC; the echo is added by the caller");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TrySendChat_Rejects_BlankOrNullMessage(string? message)
    {
        CnCNetGameRoomSession room = MakeRoom();

        CnCNetGameRoomSession.RoomChatSendResult result = room.TrySendChat(message!, ircColorId: 0);

        result.Should().Be(CnCNetGameRoomSession.RoomChatSendResult.Failed);
    }

    [Fact]
    public void TrySendChat_SlashCommand_HandledWithoutConnection()
    {
        // Slash commands do not need IRC; they emit room notices locally.
        CnCNetGameRoomSession room = MakeRoom();

        CnCNetGameRoomSession.RoomChatSendResult result = room.TrySendChat("/roll 1d6", ircColorId: 0);

        result.Should().Be(CnCNetGameRoomSession.RoomChatSendResult.HandledAsCommand);
        room.ChatLines.Should().ContainSingle(l => l.IsSystem && l.DisplayText.StartsWith("Dice roll"));
    }

    [Fact]
    public void ChatLines_ReturnsSnapshot_NotLiveReference()
    {
        // Callers iterate ChatLines from UI threads; mutations from the IRC thread must
        // not surface as InvalidOperationException. Verify we hand back a copy.
        CnCNetGameRoomSession room = MakeRoom();
        room.AppendRemoteChat("Alice", "first");

        var snapshot = room.ChatLines;
        room.AppendRemoteChat("Bob", "second");

        snapshot.Should().HaveCount(1, "snapshot taken before the second append must be frozen");
        room.ChatLines.Should().HaveCount(2);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static CnCNetGameRoomSession MakeRoom()
    {
        var activeRoom = new CnCNetActiveGameRoom
        {
            RoomName = "Test Room",
            ChannelName = "#test-room",
            Password = string.Empty,
            Tunnel = new CnCNetTunnel(),
            HostName = "HostGuy",
            IsHost = true,
            MaxPlayers = 4,
        };
        return new CnCNetGameRoomSession(activeRoom);
    }

    /// <summary>Minimal TempGameRoot wrapper so ProgramConstants game-root is bound for the test.</summary>
    private sealed class TempGameRootFixture : IDisposable
    {
        private readonly Fixture.TempGameRoot _root = new();

        public TempGameRootFixture() => _root.BindToProgramConstants();

        public void Dispose() => _root.Dispose();
    }
}
