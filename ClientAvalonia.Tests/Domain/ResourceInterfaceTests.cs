using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Domain;

public sealed class ResourceInterfaceTests
{
    [Fact]
    public void MapEntry_Implements_IMapResource_Metadata()
    {
        var map = new MapEntry
        {
            BaseFilePath = "maps/test.map",
            DisplayName = "Test",
            UntranslatedName = "Test",
            GameModes = ["Standard"],
            Sha1 = "abc123",
            CompleteFilePath = @"C:\game\maps\test.map",
            IsOfficial = true,
            MaxPlayers = 4,
        };

        IMapResource resource = map;
        resource.LogicalId.Should().Be("abc123");
        resource.FilePath.Should().Be(@"C:\game\maps\test.map");
        resource.Origin.Should().Be(ResourceOrigin.Official);
        resource.IsReadOnly.Should().BeTrue();
        resource.MaxPlayers.Should().Be(4);
        resource.Version.Should().Be(new VersionInfo(0, 0, 0, 0));
    }

    [Fact]
    public void MissionEntry_Implements_IMissionResource()
    {
        var mission = new MissionEntry
        {
            SectionName = "MISSION1",
            DisplayName = "First",
            Scenario = "scg01ea.map",
        };

        IMissionResource resource = mission;
        resource.LogicalId.Should().Be("MISSION1");
        resource.UntranslatedName.Should().Be("MISSION1");
        resource.IsHeader.Should().BeFalse();
        resource.ModMetadata.Should().BeEmpty();
    }

    [Fact]
    public void GameModeEntry_Implements_IGameModeResource()
    {
        var mode = new GameModeEntry
        {
            Name = "Standard",
            DisplayName = "标准",
            UntranslatedUIName = "Standard",
        };

        IGameModeResource resource = mode;
        resource.LogicalId.Should().Be("Standard");
        resource.UntranslatedName.Should().Be("Standard");
        resource.Name.Should().Be("Standard");
    }

    [Fact]
    public void LobbyPlayerSlot_Implements_IPlayerSlot()
    {
        var slot = new LobbyPlayerSlot { Name = "P1", ColorIndex = 2 };
        IPlayerSlot iface = slot;
        iface.ColorIndex.Should().Be(2);
        iface.IsOccupied.Should().BeTrue();
    }
}
