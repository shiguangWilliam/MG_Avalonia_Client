using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Unit matrix: can Avalonia join a room hosted by DX / MG / Avalonia (and can DX join Avalonia)?
///
/// Models IRC +k without a live server: host key must appear in the joiner candidate list
/// (or match on the first attempt for DX↔Avalonia).
/// </summary>
public sealed class CnCNetCrossClientRoomJoinTests
{
    private const string Channel = DxAliases.SampleChannel;
    private const string Room = DxAliases.SampleRoomName;

    [Fact]
    [Trait("Baseline", "DX")]
    [Trait("DXContract", "DX-PASSWORD-SHA1-CHANNEL")]
    public void AvaloniaHost_DefaultKey_Equals_DxHostKey()
    {
        string avaloniaHost = CnCNetLobbyOperations.GetDefaultChannelPassword(Channel, Room);
        string dxHost = DxHostKey(Channel);

        avaloniaHost.Should().Be(dxHost,
            "Avalonia create/host must use DX SHA1(channel) so DX clients join on first attempt");
    }

    [Fact]
    [Trait("Baseline", "DX")]
    public void DxHost_AvaloniaJoiner_MatchesOnFirstCandidate()
    {
        string hostKey = DxHostKey(Channel);
        SimulateAvaloniaJoin(Channel, Room, out string firstAttempt, out IReadOnlyList<string> candidates);

        firstAttempt.Should().Be(hostKey);
        candidates[0].Should().Be(hostKey);
        JoinerCanEnter(hostKey, candidates).Should().BeTrue();
    }

    [Fact]
    [Trait("Baseline", "DX")]
    public void AvaloniaHost_DxJoiner_MatchesWithDxFormulaAlone()
    {
        // Avalonia hosts with ResolveCreatePassword → DX key.
        CnCNetLobbyOperations.ResolveCreatePassword(
            Channel, Room, requiresPassword: false, password: "",
            out string hostKey, out bool isCustom);

        isCustom.Should().BeFalse();
        DxHostKey(Channel).Should().Be(hostKey,
            "a pure DX joiner only knows SHA1(channel); that must equal Avalonia host +k");
    }

    [Fact]
    [Trait("Baseline", "MG-Binary")]
    public void MgHost_AvaloniaJoiner_SucceedsViaFallbackCandidate()
    {
        string hostKey = MgHostKey(Channel, Room);
        SimulateAvaloniaJoin(Channel, Room, out string firstAttempt, out IReadOnlyList<string> candidates);

        firstAttempt.Should().NotBe(hostKey, "primary attempt is DX; MG hosts need fallback");
        candidates.Should().Contain(hostKey);
        candidates.ToList().IndexOf(hostKey).Should().BeGreaterThan(0);
        JoinerCanEnter(hostKey, candidates).Should().BeTrue();
    }

    [Fact]
    public void AvaloniaHost_AvaloniaJoiner_MatchesOnFirstCandidate()
    {
        string hostKey = CnCNetLobbyOperations.GetDefaultChannelPassword(Channel, Room);
        SimulateAvaloniaJoin(Channel, Room, out string firstAttempt, out IReadOnlyList<string> candidates);

        firstAttempt.Should().Be(hostKey);
        JoinerCanEnter(hostKey, candidates).Should().BeTrue();
    }

    [Fact]
    public void CustomPasswordRoom_BothSides_UseSameUserPassword()
    {
        const string custom = "SecretPass";

        CnCNetLobbyOperations.ResolveCreatePassword(
            Channel, Room, requiresPassword: true, password: custom,
            out string hostKey, out bool isCustom).Should().BeTrue();
        isCustom.Should().BeTrue();
        hostKey.Should().Be(custom);

        var listing = MakeListing(Channel, Room, requiresPassword: true);
        CnCNetLobbyOperations.TryResolveJoinPassword(
            listing, custom, out string joinKey, out IReadOnlyList<string>? candidates, out _)
            .Should().BeTrue();

        joinKey.Should().Be(custom);
        candidates.Should().BeNull("custom-password rooms do not use default +k candidates");
    }

    [Theory]
    [InlineData("#cncnet-mo", 1234567)]
    [InlineData("cncnet-yr", 9999999)]
    [InlineData("#cncnet-dta", 1000000)]
    [Trait("Baseline", "DX")]
    public void BuildGameChannelName_MatchesDxRandomizeChannelName_Format(string chat, int suffix)
    {
        string channel = CnCNetLobbyOperations.BuildGameChannelName(chat, suffix);

        channel.Should().StartWith("#");
        channel.Should().Contain("-game");
        channel.Should().NotContain("游戏", "DX uses English -game, not L10N");
        channel.Should().EndWith(suffix.ToString(System.Globalization.CultureInfo.InvariantCulture));
        channel.Should().MatchRegex(@"^#.+-game\d{7}$");
    }

    [Fact]
    public void TryGetEnglishGameChannelName_ConvertsMgLocalizedSuffix()
    {
        string? english = CnCNetLobbyOperations.TryGetEnglishGameChannelName("#cncnet-mo-游戏7654321");
        english.Should().Be("#cncnet-mo-game7654321");
    }

    private static void SimulateAvaloniaJoin(
        string channel,
        string room,
        out string firstAttempt,
        out IReadOnlyList<string> candidates)
    {
        var listing = MakeListing(channel, room, requiresPassword: false);
        CnCNetLobbyOperations.TryResolveJoinPassword(
            listing, userPassword: null, out firstAttempt, out IReadOnlyList<string>? c, out string? error)
            .Should().BeTrue(error);
        candidates = c ?? throw new InvalidOperationException("expected default candidates");
    }

    private static CnCNetHostedGameSummary MakeListing(string channel, string room, bool requiresPassword)
        => new()
        {
            HostName = "Host",
            RoomName = room,
            ChannelName = channel,
            RequiresPassword = requiresPassword,
            MaxPlayers = 8,
            PlayerCount = 1,
            Players = ["Host"],
        };

    private static bool JoinerCanEnter(string hostKey, IReadOnlyList<string> candidates)
        => candidates.Any(c => c.Equals(hostKey, StringComparison.Ordinal));

    /// <summary>DXMainClient CnCNetLobby: SHA1(channelName).Substring(0, 10).</summary>
    private static string DxHostKey(string channel)
        => Sha1First10(CnCNetIrcChannelNames.Preserve(channel));

    /// <summary>MG clientdx.exe: SHA1(channelName + GameRoomName).Substring(0, 10).</summary>
    private static string MgHostKey(string channel, string room)
        => Sha1First10(CnCNetIrcChannelNames.Preserve(channel) + room);

    private static string Sha1First10(string input)
    {
        byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant()[..DxAliases.PasswordHashHexPrefixLength];
    }
}
