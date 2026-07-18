using System;
using System.IO;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Integration;

/// <summary>
/// End-to-end (offline) CnCNet join readiness for MG and LNOD workspaces:
/// disk layout → channel funnel → welcome JOIN plan.
/// Does not open a live IRC socket (see Category=Integration; CI-safe).
/// </summary>
[Collection("ProgramConstantsSerial")]
[Trait("Category", "Integration")]
[Trait("DXContract", "LNOD-DX-CHANNEL-CONVENTION")]
public sealed class CnCNetMgAndLnodJoinIntegrationTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public void Dispose()
    {
        ClientConfiguration.ResetInstance();
        _root.Dispose();
    }

    [Fact]
    public void MgWorkspace_ResolvesYuanmingChannels_AndIsLobbyReady()
    {
        WriteMgWorkspace();
        Bind();

        var collection = new CnCNetGameCollection();
        collection.Initialize();

        CnCNetGameEntry? local = collection.GetLocalGame();
        local.Should().NotBeNull("MG CustomGames must yield LocalGame=mg");
        local!.InternalName.Should().Be("mg");
        local.ChatChannel.Should().Be("#yuanming-games");
        local.GameBroadcastChannel.Should().Be("#yuanming-cg-games");

        CnCNetWelcomeChannelPlan.IsLobbyReady(local).Should().BeTrue();

        var joins = CnCNetWelcomeChannelPlan.BuildForLocalGame(local);
        joins.Select(j => j.Channel).Should().Equal(
            "#yuanming-games",
            "#cncnet",
            "#yuanming-cg-games");
        joins[0].Key.Should().Be(CnCNetWelcomeChannelPlan.DefaultChatChannelKey);
        joins[1].Key.Should().Be(CnCNetWelcomeChannelPlan.DefaultChatChannelKey);
        joins[2].Key.Should().BeNull("broadcast channels join without +k");

        collection.FindByBroadcastChannel("#yuanming-cg-games")!
            .InternalName.Should().Be("mg");
    }

    [Fact]
    public void LnodWorkspace_SynthesizesCncnetLnodChannels_MatchingDxJoinLog()
    {
        WriteLnodWorkspace();
        Bind();

        var collection = new CnCNetGameCollection();
        collection.Initialize();

        CnCNetGameEntry? local = collection.GetLocalGame();
        local.Should().NotBeNull("empty Collection must fall through to LocalGame convention");
        local!.InternalName.Should().Be("lnod");
        // LNOD DX client.log 16:04: JOIN #cncnet-lnod-games / #cncnet-lnod / #cncnet
        local.ChatChannel.Should().Be("#cncnet-lnod");
        local.GameBroadcastChannel.Should().Be("#cncnet-lnod-games");

        CnCNetWelcomeChannelPlan.IsLobbyReady(local).Should().BeTrue();

        var joins = CnCNetWelcomeChannelPlan.BuildForLocalGame(local);
        joins.Select(j => (j.Channel, j.Key)).Should().Equal(
            ("#cncnet-lnod", CnCNetWelcomeChannelPlan.DefaultChatChannelKey),
            ("#cncnet", CnCNetWelcomeChannelPlan.DefaultChatChannelKey),
            ("#cncnet-lnod-games", null));
    }

    [Fact]
    public void BothWorkspaces_CanJoinCnCNet_Independently()
    {
        // MG
        WriteMgWorkspace();
        Bind();
        AssertLobbyReady(
            expectedId: "mg",
            expectedChat: "#yuanming-games",
            expectedBroadcast: "#yuanming-cg-games");

        // LNOD (reuse same TempGameRoot after rewrite)
        WriteLnodWorkspace();
        Bind();
        AssertLobbyReady(
            expectedId: "lnod",
            expectedChat: "#cncnet-lnod",
            expectedBroadcast: "#cncnet-lnod-games");
    }

    [Fact]
    public void Lnod_WithChannelsFollowedInSettings_StillUsesSynthesizedNames()
    {
        WriteLnodWorkspace();
        // RA2MD-style follow flag — must NOT redefine IRC names.
        File.WriteAllText(
            Path.Combine(_root.GameRoot, "RA2MD.ini"),
            """
            [Channels]
            LNOD=True
            YR=False
            MG=False
            """);
        Bind();

        var collection = new CnCNetGameCollection();
        collection.Initialize();
        CnCNetGameEntry? local = collection.GetLocalGame();
        local.Should().NotBeNull();
        local!.ChatChannel.Should().Be("#cncnet-lnod");
        local.GameBroadcastChannel.Should().Be("#cncnet-lnod-games");
        CnCNetWelcomeChannelPlan.IsLobbyReady(local).Should().BeTrue();
    }

    private static void AssertLobbyReady(string expectedId, string expectedChat, string expectedBroadcast)
    {
        var collection = new CnCNetGameCollection();
        collection.Initialize();
        CnCNetGameEntry? local = collection.GetLocalGame();
        local.Should().NotBeNull();
        local!.InternalName.Should().Be(expectedId);
        local.ChatChannel.Should().Be(expectedChat);
        local.GameBroadcastChannel.Should().Be(expectedBroadcast);
        CnCNetWelcomeChannelPlan.IsLobbyReady(local).Should().BeTrue(
            $"{expectedId} must be able to JOIN chat + #cncnet + broadcast");
        collection.GetSelectableGames()
            .Should().Contain(g => g.InternalName == expectedId);
    }

    private void Bind()
    {
        _root.BindToProgramConstants();
        ClientConfiguration.ResetInstance();
    }

    private void WriteMgWorkspace()
    {
        File.WriteAllText(
            _root.ClientDefinitionsPath,
            """
            [Settings]
            LocalGame=MG
            LongGameName=创世之刻
            SettingsFile=RA2MG.ini
            CnCNetLiveStatusIdentifier=cncnet5_mg
            """);

        File.WriteAllText(
            Path.Combine(_root.ResourcesPath, "GameCollectionConfig.ini"),
            """
            [CustomGames]
            0=CustomGame

            [CustomGame]
            InternalName=MG
            UIName=创世之刻
            ChatChannel=#yuanming-games
            GameBroadcastChannel=#yuanming-cg-games
            IconFilename=friendicon.png
            """);

        File.WriteAllText(
            Path.Combine(_root.GameRoot, "RA2MG.ini"),
            """
            [Channels]
            MG=True
            """);
    }

    private void WriteLnodWorkspace()
    {
        File.WriteAllText(
            _root.ClientDefinitionsPath,
            """
            [Settings]
            LocalGame=lnod
            LongGameName=La Nuova origine del destino
            SettingsFile=RA2MD.ini
            CnCNetLiveStatusIdentifier=cncnet5_lnod
            """);

        // Empty CustomGames — same as D:\MG\LNod5.15\Resources\GameCollectionConfig.ini
        File.WriteAllText(
            Path.Combine(_root.ResourcesPath, "GameCollectionConfig.ini"),
            """
            ; List of custom CnCNet games / mods.
            [CustomGames]
            """);

        File.WriteAllText(
            Path.Combine(_root.GameRoot, "RA2MD.ini"),
            """
            [Channels]
            LNOD=True
            """);
    }
}
