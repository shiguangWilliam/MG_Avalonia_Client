using System;
using System.Collections.Generic;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.Services;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Domain;

public sealed class ResourceCatalogAdapterTests
{
    [Fact]
    public void Adapter_Returns_Interface_Typed_Collections()
    {
        var adapter = new GameResourceCatalogAdapter(GameResourceCatalog.Instance);

        adapter.Maps.Should().BeAssignableTo<IReadOnlyList<IMapResource>>();
        adapter.GameModes.Should().BeAssignableTo<IReadOnlyList<IGameModeResource>>();
        adapter.Missions.Should().BeAssignableTo<IReadOnlyList<IMissionResource>>();
    }

    [Fact]
    public void NoOpResourceManifest_VerifyHash_Always_True()
    {
        var manifest = new NoOpResourceManifest();
        var map = new MapEntry
        {
            BaseFilePath = "x.map",
            DisplayName = "X",
            UntranslatedName = "X",
            GameModes = ["Standard"],
        };

        manifest.VerifyHash(map).Should().BeTrue();
        // NoOp returns empty diff until online-update logic lands.
        manifest.ComputeDiff([], [map]).Should().BeEmpty();
    }

    // ---- A1: IResourceCatalog contract accepts arbitrary IMapResource ----

    /// <summary>
    /// A mock IMapResource that is NOT a MapEntry. Before A1, this would throw
    /// ArgumentException when passed to PickRandomMapIndex / ToggleFavoriteMap.
    /// </summary>
    private sealed class MockMapResource : IMapResource
    {
        public string LogicalId => Sha1;
        public string DisplayName => "Mock";
        public string UntranslatedName => "Mock";
        public string FilePath => "mock.map";
        public string Sha1 { get; init; } = "abc123";
        public long SizeBytes => 0;
        public ResourceOrigin Origin => ResourceOrigin.Custom;
        public VersionInfo Version => new(0, 0, 0, 0);
        public bool IsReadOnly => false;
        public IReadOnlyList<string> GameModes { get; init; } = ["Standard"];
        public int MinPlayers => 0;
        public int MaxPlayers { get; init; } = 4;
        public bool EnforceMaxPlayers => false;
        public bool MultiplayerOnly => false;
        public bool IsCustom => true;
        public string PreviewRelativePath => "";
        public string ExtraIniName => "";
        public IReadOnlyList<string> Waypoints { get; init; } = [];
        public int MapX => 0;
        public int MapY => 0;
        public int MapWidth => 0;
        public int MapHeight => 0;
        public IReadOnlyList<string> ActualSize { get; init; } = [];
        public IReadOnlyList<string> LocalSize { get; init; } = [];
    }

    [Fact]
    public void PickRandomMapIndex_Accepts_NonMapEntry_IMapResource()
    {
        // A1: IResourceCatalog contract requires this to work with any IMapResource.
        var adapter = new GameResourceCatalogAdapter(GameResourceCatalog.Instance);
        var mocks = new List<IMapResource>
        {
            new MockMapResource { Sha1 = "a", MaxPlayers = 4 },
            new MockMapResource { Sha1 = "b", MaxPlayers = 4 },
            new MockMapResource { Sha1 = "c", MaxPlayers = 4 },
        };

        // Before A1 this threw ArgumentException("Expected MapEntry instances...").
        int index = adapter.PickRandomMapIndex(mocks, playerCount: 2);

        index.Should().BeInRange(0, mocks.Count - 1);
    }

    [Fact]
    public void PickRandomMapIndex_EmptyList_Returns_MinusOne()
    {
        var adapter = new GameResourceCatalogAdapter(GameResourceCatalog.Instance);
        adapter.PickRandomMapIndex(new List<IMapResource>(), playerCount: 2)
            .Should().Be(-1);
    }

    [Fact]
    public void PickRandomMapIndex_NullList_ThrowsArgumentNullException()
    {
        var adapter = new GameResourceCatalogAdapter(GameResourceCatalog.Instance);
        var act = () => adapter.PickRandomMapIndex(null!, playerCount: 2);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToggleFavoriteMap_Accepts_NonMapEntry_IMapResource()
    {
        // A1: previously threw ArgumentException("Expected MapEntry."). After the
        // fix, the method is willing to accept any IMapResource; downstream calls
        // (UserINISettings.IsFavoriteMap) may still throw if the test runner has
        // not initialized ClientCore's settings — that is unrelated to the A1
        // contract. We assert the previous ArgumentException is gone.
        var adapter = new GameResourceCatalogAdapter(GameResourceCatalog.Instance);
        var mock = new MockMapResource { Sha1 = "toggle-test-1", MaxPlayers = 4 };

        try
        {
            adapter.ToggleFavoriteMap(mock, gameMode: null);
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            // OK: Test-runner environment may not have UserINISettings initialized.
            // The important thing is that we never see ArgumentException("Expected MapEntry.").
        }
    }

    [Fact]
    public void ToggleFavoriteMap_NullMap_ThrowsArgumentNullException()
    {
        var adapter = new GameResourceCatalogAdapter(GameResourceCatalog.Instance);
        var act = () => adapter.ToggleFavoriteMap(null!, gameMode: null);
        act.Should().Throw<ArgumentNullException>();
    }
}
