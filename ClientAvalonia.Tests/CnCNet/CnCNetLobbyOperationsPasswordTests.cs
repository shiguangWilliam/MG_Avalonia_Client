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
/// Avalonia aligns create/host and primary join with <b>DX</b>.
/// <see cref="CnCNetLobbyOperations.GetDefaultChannelPasswordCandidates"/> returns DX first,
/// then MG channel+room as a join fallback so MG-hosted rooms remain joinable.
/// </summary>
public sealed class CnCNetLobbyOperationsPasswordTests
{
    // Locked magic values: SHA1 hex of ASCII input, first 10 chars, lower-case.
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
        // Reference: MG clientdx.exe IL (verified against Client/client.log)
        //   string password = SHA1(channelName + GameRoomName).Substring(0, 10);
        string recomputed = Sha1First10(DxAliases.SampleChannel + DxAliases.SampleRoomName);
        recomputed.Should().Be(MgChannelPlusRoomHash,
            "MG actual uses SHA1(channelName + roomName) — if this changes, MG IL has drifted.");
    }

    [Fact]
    [Trait("Baseline", "DX")]
    public void GetDefaultChannelPassword_Returns_DxChannelOnly_Hash_First()
    {
        string key = CnCNetLobbyOperations.GetDefaultChannelPassword(
            DxAliases.SampleChannel, DxAliases.SampleRoomName);
        key.Should().Be(DxChannelOnlyHash);
    }

    [Fact]
    public void JoinPasswordCandidates_Order_DxFirst_ThenMgChannelPlusRoom()
    {
        var candidates = CnCNetLobbyOperations.GetDefaultChannelPasswordCandidates(
            DxAliases.SampleChannel, DxAliases.SampleRoomName);

        candidates.Should().NotBeEmpty();
        candidates[0].Should().Be(DxChannelOnlyHash, "DX upstream is always first");
        candidates.Should().Contain(MgChannelPlusRoomHash, "MG channel+room is a join fallback");
        int mgIndex = candidates.ToList().IndexOf(MgChannelPlusRoomHash);
        mgIndex.Should().BeGreaterThan(0, "MG hash must come AFTER the DX hash");
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
        ircKey.Should().Be(DxChannelOnlyHash);
        isCustomPassword.Should().BeFalse();
    }

    [Fact]
    public void ResolveCreatePassword_DerivesDefaultKey_WhenPasswordRequiredButBlank()
    {
        bool ok = CnCNetLobbyOperations.ResolveCreatePassword(
            DxAliases.SampleChannel, DxAliases.SampleRoomName,
            requiresPassword: true, password: "   ",
            out string ircKey, out bool isCustomPassword);

        ok.Should().BeTrue();
        ircKey.Should().Be(DxChannelOnlyHash);
        isCustomPassword.Should().BeFalse();
    }

    [Fact]
    public void BuildChannelPasswordModeCommand_SwitchesKeyCleanly()
    {
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
        joinPassword.Should().Be(DxChannelOnlyHash);
        candidates.Should().NotBeNull();
        candidates![0].Should().Be(DxChannelOnlyHash);
        candidates.Should().Contain(MgChannelPlusRoomHash);
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
