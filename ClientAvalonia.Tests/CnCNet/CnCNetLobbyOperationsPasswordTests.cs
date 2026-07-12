using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// DX vs MG password divergence — the most subtle contract in the codebase.
///
/// DX (DXMainClient CnCNetLobby.cs:1052): host IRC +k key = first 10 hex of
///   <c>SHA1(ASCII(channelName))</c>.
/// MG (clientdx.exe IL + Client/client.log): host IRC +k key = first 10 hex of
///   <c>SHA1(ASCII(channelName + GameRoomName))</c>.
///
/// <see cref="CnCNetLobbyOperations.GetDefaultChannelPasswordCandidates"/> returns MG-actual
/// first, then DX-upstream as a fallback, so a host on either side can be rejoined.
///
/// Tests pin BOTH baselines with magic values computed from the same algorithm — if either
/// side regresses the SHA1 input or truncation length, the test fails loudly.
/// </summary>
public sealed class CnCNetLobbyOperationsPasswordTests
{
    // Locked magic values: SHA1 hex of ASCII input, first 10 chars, lower-case.
    // Verified once via a one-shot test (see git history); pinned here as regression locks.
    private const string DxChannelOnlyHash = "5cbd0e1a1e";      // SHA1("#ra3-game-1234567")[..10]
    private const string MgChannelPlusRoomHash = "d6a95d9ccc";  // SHA1("#ra3-game-1234567TestRoom")[..10]

    [Fact]
    [Trait("Baseline", "DX")]
    public void DxBehavior_Sha1OfChannelName_First10Hex()
    {
        // Reference: DXMainClient/DXGUI/Multiplayer/CnCNet/CnCNetLobby.cs:1052
        //   string password = SHA1(channelName).Substring(0, 10);
        string recomputed = Sha1First10(DxAliases.SampleChannel);
        recomputed.Should().Be(DxChannelOnlyHash,
            "DX upstream uses SHA1(channelName) — if this changes, the upstream reference has moved.");
    }

    [Fact]
    [Trait("Baseline", "MG-Binary")]
    public void MgBehavior_Sha1OfChannelPlusRoomName_First10Hex()
    {
        // Reference: MG clientdx.exe IL (verified in prior session against Client/client.log)
        //   string password = SHA1(channelName + GameRoomName).Substring(0, 10);
        // with ASCII encoding (non-ASCII bytes collapse to '?').
        string recomputed = Sha1First10(DxAliases.SampleChannel + DxAliases.SampleRoomName);
        recomputed.Should().Be(MgChannelPlusRoomHash,
            "MG actual uses SHA1(channelName + roomName) — if this changes, MG IL has drifted.");
    }

    [Fact]
    [Trait("Baseline", "MG-Binary")]
    public void GetDefaultChannelPassword_Returns_MgChannelPlusRoom_Hash_First()
    {
        // MG is the host this client targets, so MG-actual is the first/default key.
        string key = CnCNetLobbyOperations.GetDefaultChannelPassword(
            DxAliases.SampleChannel, DxAliases.SampleRoomName);
        key.Should().Be(MgChannelPlusRoomHash);
    }

    [Fact]
    public void JoinPasswordCandidates_Order_ChannelFirst_ThenChannelPlusRoom()
    {
        // MG-actual first, then DX-upstream fallback, then codepage fallbacks for non-ASCII.
        // For pure-ASCII inputs (the common case) we expect exactly two candidates.
        var candidates = CnCNetLobbyOperations.GetDefaultChannelPasswordCandidates(
            DxAliases.SampleChannel, DxAliases.SampleRoomName);

        candidates.Should().NotBeEmpty();
        candidates[0].Should().Be(MgChannelPlusRoomHash, "MG actual is always first");
        candidates.Should().Contain(DxChannelOnlyHash, "DX upstream is a fallback");
        int dxIndex = candidates.ToList().IndexOf(DxChannelOnlyHash);
        dxIndex.Should().BeGreaterThan(0, "DX hash must come AFTER the MG hash, not before");
    }

    [Fact]
    public void JoinPasswordCandidates_AreUnique()
    {
        var candidates = CnCNetLobbyOperations.GetDefaultChannelPasswordCandidates(
            DxAliases.SampleChannel, DxAliases.SampleRoomName);
        candidates.Distinct().Count().Should().Be(candidates.Count);
    }

    [Fact]
    public void JoinPasswordCandidates_AddCodepageFallbacks_ForNonAsciiChannel()
    {
        // When channel or room contains non-ASCII chars, ASCII encoding turns them into '?'.
        // The codepage fallback (Encoding.Default) catches hosts that hash the original bytes.
        var candidates = CnCNetLobbyOperations.GetDefaultChannelPasswordCandidates(
            "#游戏-1", "TestRoom");
        candidates.Count.Should().BeGreaterThan(2, "non-ASCII inputs add codepage fallbacks");
    }

    [Fact]
    public void ResolveCreatePassword_UsesCustomPassword_WhenRequiredAndProvided()
    {
        bool ok = CnCNetLobbyOperations.ResolveCreatePassword(
            DxAliases.SampleChannel, DxAliases.SampleRoomName,
            requiresPassword: true, password: "MySecret",
            out string ircKey, out bool isCustomPassword);

        ok.Should().BeTrue();
        ircKey.Should().Be("MySecret");
        isCustomPassword.Should().BeTrue();
    }

    [Fact]
    public void ResolveCreatePassword_DerivesDefaultKey_WhenNoUserPassword()
    {
        bool ok = CnCNetLobbyOperations.ResolveCreatePassword(
            DxAliases.SampleChannel, DxAliases.SampleRoomName,
            requiresPassword: false, password: "",
            out string ircKey, out bool isCustomPassword);

        ok.Should().BeTrue();
        ircKey.Should().Be(MgChannelPlusRoomHash);
        isCustomPassword.Should().BeFalse();
    }

    [Fact]
    public void ResolveCreatePassword_DerivesDefaultKey_WhenPasswordRequiredButBlank()
    {
        // Empty user password but RequiresPassword → fall back to derived MG key.
        bool ok = CnCNetLobbyOperations.ResolveCreatePassword(
            DxAliases.SampleChannel, DxAliases.SampleRoomName,
            requiresPassword: true, password: "   ",
            out string ircKey, out bool isCustomPassword);

        ok.Should().BeTrue();
        ircKey.Should().Be(MgChannelPlusRoomHash);
        isCustomPassword.Should().BeFalse();
    }

    [Fact]
    public void BuildChannelPasswordModeCommand_SwitchesKeyCleanly()
    {
        // DX Channel.ChangePassword: -k old, +k new, or -k+k old new.
        CnCNetLobbyOperations.BuildChannelPasswordModeCommand("#c", "old", "new")
            .Should().Be("MODE #c -k+k old new");
        CnCNetLobbyOperations.BuildChannelPasswordModeCommand("#c", "", "new")
            .Should().Be("MODE #c +k new");
        CnCNetLobbyOperations.BuildChannelPasswordModeCommand("#c", "old", "")
            .Should().Be("MODE #c -k old");
    }

    [Fact]
    public void TryResolveJoinPassword_ReturnsDefaultCandidates_WhenGameNotPassworded()
    {
        // MG CnCNetLobby.JoinGame: always derives from channelName+roomName, ignores stale user input.
        var game = new CnCNetHostedGameSummary
        {
            HostName = "H",
            RoomName = DxAliases.SampleRoomName,
            ChannelName = DxAliases.SampleChannel,
            RequiresPassword = false,
        };

        bool ok = CnCNetLobbyOperations.TryResolveJoinPassword(
            game, userPassword: null, out string joinPassword, out var candidates, out _);

        ok.Should().BeTrue();
        joinPassword.Should().Be(MgChannelPlusRoomHash);
        candidates.Should().NotBeNull();
        candidates![0].Should().Be(MgChannelPlusRoomHash);
    }

    [Fact]
    public void TryResolveJoinPassword_RequiresUserPassword_WhenGamePassworded()
    {
        var game = new CnCNetHostedGameSummary
        {
            HostName = "H",
            RoomName = DxAliases.SampleRoomName,
            ChannelName = DxAliases.SampleChannel,
            RequiresPassword = true,
        };

        bool ok = CnCNetLobbyOperations.TryResolveJoinPassword(
            game, userPassword: "", out _, out _, out string? error);

        ok.Should().BeFalse();
        error.Should().Contain("password");
    }

    [Fact]
    public void TryResolveJoinPassword_UsesUserPassword_WhenGamePassworded_AndProvided()
    {
        var game = new CnCNetHostedGameSummary
        {
            HostName = "H",
            RoomName = DxAliases.SampleRoomName,
            ChannelName = DxAliases.SampleChannel,
            RequiresPassword = true,
        };

        bool ok = CnCNetLobbyOperations.TryResolveJoinPassword(
            game, userPassword: "  MyPass  ", out string joinPassword, out _, out _);

        ok.Should().BeTrue();
        joinPassword.Should().Be("MyPass", "user password is trimmed");
    }

    private static string Sha1First10(string input)
    {
        byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant()[..10];
    }
}
