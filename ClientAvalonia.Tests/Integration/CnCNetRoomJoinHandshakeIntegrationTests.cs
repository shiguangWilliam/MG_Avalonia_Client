using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClientAvalonia.CnCNet;
using ClientAvalonia.CnCNet.Protocol;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Tests.Fixture;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Integration;

/// <summary>
/// System-level create → GAME listing → join handshake for default-password rooms,
/// without a live IRC server.
///
/// Simulates host sides (DX formula / Avalonia ResolveCreatePassword / MG formula)
/// and an Avalonia joiner (TryResolveJoinPassword + candidate retry), asserting that
/// both clients can occupy the same room (+k match).
/// </summary>
[Collection("ProgramConstantsSerial")]
[Trait("Category", "Integration")]
[Trait("DXContract", "DX-PASSWORD-SHA1-CHANNEL")]
public sealed class CnCNetRoomJoinHandshakeIntegrationTests : IDisposable
{
    private readonly TempGameRoot _root = new();
    private readonly string _originalRevision = ProgramConstants.CNCNET_PROTOCOL_REVISION;
    private readonly string _originalGameVersion = ProgramConstants.GAME_VERSION;

    public CnCNetRoomJoinHandshakeIntegrationTests()
    {
        _root.BindToProgramConstants();
        ProgramConstants.ApplyCnCNetProtocolRevision(DxAliases.CurrentProtocolRevision);
        ProgramConstants.GAME_VERSION = "1.0";
    }

    public void Dispose()
    {
        ProgramConstants.ApplyCnCNetProtocolRevision(_originalRevision);
        ProgramConstants.GAME_VERSION = _originalGameVersion;
        _root.Dispose();
    }

    [Fact]
    public void Handshake_AvaloniaHost_DxJoiner_SameRoomKey()
    {
        string channel = CnCNetLobbyOperations.BuildGameChannelName("#cncnet-mo", 2_345_678);
        const string room = "AvaloniaHostRoom";

        CnCNetLobbyOperations.ResolveCreatePassword(
            channel, room, requiresPassword: false, password: "",
            out string hostKey, out bool passworded).Should().BeTrue();
        passworded.Should().BeFalse();

        CnCNetHostedGameSummary listing = ParseOpenListing(
            hostName: "AvaloniaHost",
            channel: channel,
            room: room,
            passworded: false);

        string dxJoinKey = Sha1First10(CnCNetIrcChannelNames.Preserve(listing.ChannelName));

        dxJoinKey.Should().Be(hostKey);
        listing.ChannelName.Should().Be(channel);
        listing.RoomName.Should().Be(room);
        listing.RequiresPassword.Should().BeFalse();
    }

    [Fact]
    public void Handshake_DxHost_AvaloniaJoiner_SameRoomKey_FirstAttempt()
    {
        string channel = CnCNetLobbyOperations.BuildGameChannelName("#cncnet-yr", 4_444_444);
        const string room = "DxHostRoom";

        string hostKey = Sha1First10(CnCNetIrcChannelNames.Preserve(channel));

        CnCNetHostedGameSummary listing = ParseOpenListing("DxHost", channel, room, passworded: false);

        CnCNetLobbyOperations.TryResolveJoinPassword(
            listing, userPassword: null,
            out string joinKey, out IReadOnlyList<string>? candidates, out string? error)
            .Should().BeTrue(error);

        joinKey.Should().Be(hostKey);
        candidates.Should().NotBeNull();
        candidates![0].Should().Be(hostKey);
        SimulateIrcJoinWithCandidateRetry(hostKey, candidates!).Should().Be(0,
            "DX host must succeed on the first JOIN +k attempt");
    }

    [Fact]
    public void Handshake_MgHost_AvaloniaJoiner_SameRoomKey_AfterFallback()
    {
        string channel = "#cncnet-mo-游戏7654321";
        const string room = "MgHostRoom";

        string hostKey = Sha1First10(CnCNetIrcChannelNames.Preserve(channel) + room);

        CnCNetHostedGameSummary listing = ParseOpenListing("MgHost", channel, room, passworded: false);

        CnCNetLobbyOperations.TryResolveJoinPassword(
            listing, userPassword: null,
            out string firstKey, out IReadOnlyList<string>? candidates, out string? error)
            .Should().BeTrue(error);

        candidates.Should().NotBeNull().And.Contain(hostKey);
        firstKey.Should().NotBe(hostKey);

        int attempt = SimulateIrcJoinWithCandidateRetry(hostKey, candidates!);
        attempt.Should().BeGreaterThan(0);
        candidates![attempt].Should().Be(hostKey);
    }

    [Fact]
    public void Handshake_Mutual_AvaloniaHostAndJoiner_ShareIdenticalKey()
    {
        string channel = CnCNetLobbyOperations.BuildGameChannelName("#cncnet-dta", 1_111_111);
        const string room = "PeerRoom";

        CnCNetLobbyOperations.ResolveCreatePassword(
            channel, room, requiresPassword: false, password: "",
            out string hostKey, out _).Should().BeTrue();

        CnCNetHostedGameSummary listing = ParseOpenListing("PeerHost", channel, room, passworded: false);

        CnCNetLobbyOperations.TryResolveJoinPassword(
            listing, null, out string joinKey, out IReadOnlyList<string>? candidates, out _)
            .Should().BeTrue();

        joinKey.Should().Be(hostKey);
        SimulateIrcJoinWithCandidateRetry(hostKey, candidates!).Should().Be(0);
    }

    [Fact]
    public void Handshake_CustomPassword_SharedLiteral_BothSides()
    {
        string channel = CnCNetLobbyOperations.BuildGameChannelName("#cncnet-ts", 8_888_888);
        const string room = "LockedRoom";
        const string password = "TeamAlpha";

        CnCNetLobbyOperations.ResolveCreatePassword(
            channel, room, requiresPassword: true, password: password,
            out string hostKey, out bool isCustom).Should().BeTrue();
        isCustom.Should().BeTrue();
        hostKey.Should().Be(password);

        CnCNetHostedGameSummary listing = ParseOpenListing("LockHost", channel, room, passworded: true);

        CnCNetLobbyOperations.TryResolveJoinPassword(
            listing, password, out string joinKey, out IReadOnlyList<string>? candidates, out _)
            .Should().BeTrue();

        joinKey.Should().Be(hostKey);
        candidates.Should().BeNull();
    }

    [Fact]
    public void Handshake_EndToEnd_GameListingRoundTrip_PreservesChannelAndRoomForPassword()
    {
        string channel = CnCNetLobbyOperations.BuildGameChannelName("#cncnet-ra", 3_333_333);
        const string room = "RoundTripRoom";

        CnCNetLobbyOperations.ResolveCreatePassword(
            channel, room, requiresPassword: false, password: "",
            out string hostKey, out _).Should().BeTrue();

        string ctcp = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(
                channel: channel,
                roomName: room,
                flags: "00000",
                players: new[] { "RoundTripHost" },
                tunnelHost: "tunnel.example.com",
                tunnelPort: 50000));

        CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            "RoundTripHost",
            ctcp,
            SampleGameMessages.SampleTunnels(),
            sourceGameId: "ra",
            out CnCNetHostedGameSummary? listing,
            out string? reject).Should().BeTrue(reject);

        listing.Should().NotBeNull();
        listing!.ChannelName.Should().Be(channel);
        listing.RoomName.Should().Be(room);
        listing.RequiresPassword.Should().BeFalse();

        CnCNetLobbyOperations.TryResolveJoinPassword(
            listing, null, out string joinKey, out _, out _).Should().BeTrue();

        joinKey.Should().Be(hostKey);
    }

    /// <summary>
    /// Models IRC 475 retries: try candidates in order until host +k matches.
    /// Returns the successful candidate index.
    /// </summary>
    private static int SimulateIrcJoinWithCandidateRetry(string hostKey, IReadOnlyList<string> candidates)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Equals(hostKey, StringComparison.Ordinal))
                return i;
        }

        throw new InvalidOperationException(
            $"Joiner candidates [{string.Join(", ", candidates)}] never matched host key {hostKey}.");
    }

    private static CnCNetHostedGameSummary ParseOpenListing(
        string hostName,
        string channel,
        string room,
        bool passworded)
    {
        string flags = passworded ? "01000" : "00000";
        string ctcp = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(
                channel: channel,
                roomName: room,
                flags: flags,
                players: new[] { hostName }));

        CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            hostName,
            ctcp,
            SampleGameMessages.SampleTunnels(),
            sourceGameId: "mo",
            out CnCNetHostedGameSummary? game,
            out string? reject).Should().BeTrue(reject);

        return game!;
    }

    private static string Sha1First10(string input)
    {
        byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant()[..DxAliases.PasswordHashHexPrefixLength];
    }
}
