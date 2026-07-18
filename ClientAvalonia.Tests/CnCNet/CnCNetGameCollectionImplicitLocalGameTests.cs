using System;
using System.IO;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Full channel funnel through <see cref="CnCNetGameCollection.Initialize"/>:
/// built-in → CustomGames → ClientDefinitions → LocalGame convention.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class CnCNetGameCollectionImplicitLocalGameTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public CnCNetGameCollectionImplicitLocalGameTests()
    {
        // Default fixture: LNOD-like (empty Collection, no ClientDefinitions channel keys).
        WriteClientDefinitions(localGame: "lnod", longName: "La Nuova origine del destino");
        _root.BindToProgramConstants();
        ClientConfiguration.ResetInstance();
    }

    public void Dispose()
    {
        ClientConfiguration.ResetInstance();
        _root.Dispose();
    }

    [Fact]
    [Trait("Baseline", "LNOD-DX")]
    public void Initialize_LnodLike_SynthesizesCncnetLnodChannels()
    {
        var collection = new CnCNetGameCollection();
        collection.Initialize();

        CnCNetGameEntry? local = collection.GetLocalGame();
        local.Should().NotBeNull();
        local!.InternalName.Should().Be("lnod");
        local.ChatChannel.Should().Be("#cncnet-lnod");
        local.GameBroadcastChannel.Should().Be("#cncnet-lnod-games");
        local.UiName.Should().Be("La Nuova origine del destino");
    }

    [Fact]
    public void Initialize_ClientDefinitionsChannels_OverrideConvention()
    {
        WriteClientDefinitions(
            localGame: "lnod",
            longName: "Last Nod",
            chat: "#lnod-lobby",
            broadcast: "#lnod-games");
        ClientConfiguration.ResetInstance();

        var collection = new CnCNetGameCollection();
        collection.Initialize();

        CnCNetGameEntry? local = collection.GetLocalGame();
        local.Should().NotBeNull();
        local!.ChatChannel.Should().Be("#lnod-lobby");
        local.GameBroadcastChannel.Should().Be("#lnod-games");
    }

    [Fact]
    public void Initialize_CustomGames_TakesPriorityOverClientDefinitionsAndConvention()
    {
        WriteClientDefinitions(
            localGame: "mg",
            longName: "创世之刻",
            chat: "#should-not-use",
            broadcast: "#should-not-use-games");
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
        ClientConfiguration.ResetInstance();

        var collection = new CnCNetGameCollection();
        collection.Initialize();

        CnCNetGameEntry? local = collection.GetLocalGame();
        local.Should().NotBeNull();
        local!.InternalName.Should().Be("mg");
        local.ChatChannel.Should().Be("#yuanming-games");
        local.GameBroadcastChannel.Should().Be("#yuanming-cg-games");
    }

    [Fact]
    public void Initialize_BuiltInLocalGame_DoesNotDuplicateViaFunnel()
    {
        WriteClientDefinitions(localGame: "yr", longName: "Yuri's Revenge");
        ClientConfiguration.ResetInstance();

        var collection = new CnCNetGameCollection();
        collection.Initialize();

        collection.GetLocalGame().Should().NotBeNull();
        collection.GetLocalGame()!.ChatChannel.Should().Be("#cncnet-yr");
        collection.Games.Count(g => g.InternalName.Equals("yr", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
    }

    [Fact]
    public void GetSelectableGames_IncludesSynthesizedLocalGame()
    {
        var collection = new CnCNetGameCollection();
        collection.Initialize();

        collection.GetSelectableGames()
            .Should().Contain(g => g.InternalName == "lnod" && g.ChatChannel == "#cncnet-lnod");
    }

    private void WriteClientDefinitions(
        string localGame,
        string longName,
        string? chat = null,
        string? broadcast = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Settings]");
        sb.AppendLine($"LocalGame={localGame}");
        sb.AppendLine($"LongGameName={longName}");
        if (!string.IsNullOrEmpty(chat))
            sb.AppendLine($"CnCNetChatChannel={chat}");
        if (!string.IsNullOrEmpty(broadcast))
            sb.AppendLine($"CnCNetGameBroadcastChannel={broadcast}");
        File.WriteAllText(_root.ClientDefinitionsPath, sb.ToString());
    }
}
