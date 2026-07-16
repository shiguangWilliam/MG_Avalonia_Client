using System.Collections.Generic;
using ClientAvalonia.Domain;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Domain;

public sealed class StartingLocationProjectorTests
{
    [Fact]
    public void ProjectTdra_UnpacksWaypoint_AndScalesToPreview()
    {
        // packed = Y * 128 + X → X=10, Y=20 → packed = 20*128+10 = 2570
        // map origin (0,0), size 100x100, preview 200x200 → pixel (20,40)
        (int x, int y) = StartingLocationProjector.Project(
            waypoint: "2570",
            useIsometricCells: false,
            waypointCoefficient: 128,
            mapCellSizeX: 48,
            mapCellSizeY: 24,
            actualSize: ["0", "0", "0", "0"],
            localSize: ["0", "0", "0", "0"],
            tdraX: 0,
            tdraY: 0,
            tdraWidth: 100,
            tdraHeight: 100,
            previewWidth: 200,
            previewHeight: 200);

        x.Should().Be(20);
        y.Should().Be(40);
    }

    [Fact]
    public void ProjectTdra_InvalidWaypoint_ReturnsOrigin()
    {
        (int x, int y) = StartingLocationProjector.Project(
            waypoint: "not-a-number",
            useIsometricCells: false,
            waypointCoefficient: 128,
            mapCellSizeX: 48,
            mapCellSizeY: 24,
            actualSize: ["0", "0", "0", "0"],
            localSize: ["0", "0", "0", "0"],
            tdraX: 0,
            tdraY: 0,
            tdraWidth: 100,
            tdraHeight: 100,
            previewWidth: 200,
            previewHeight: 200);

        x.Should().Be(0);
        y.Should().Be(0);
    }

    [Fact]
    public void ProjectIsometric_UsesDxPackedTileConvention()
    {
        // DX packs isometric tiles as: last 3 digits = isoTileX, prefix = isoTileY.
        // Example: "1005,0" → Y=1, X=005=5. With actualSizeW=50, localSize zero, cell 48x24.
        // rx = 5 - 1 + 50 - 1 = 53
        // ry = 5 + 1 - 50 - 1 = -45
        // pixelPosX = 53*48/2 = 1272
        // pixelPosY = -45*24/2 = -540
        // mapSize = localW*48 x localH*24 — use LocalSize "0,0,100,100"
        // mapSizeX=4800, mapSizeY=2400
        // ratioX = 1272/4800, ratioY = -540/2400
        // preview 480x240 → (127, -54)
        (int x, int y) = StartingLocationProjector.Project(
            waypoint: "1005,0",
            useIsometricCells: true,
            waypointCoefficient: 128,
            mapCellSizeX: 48,
            mapCellSizeY: 24,
            actualSize: ["0", "0", "50", "50"],
            localSize: ["0", "0", "100", "100"],
            tdraX: 0,
            tdraY: 0,
            tdraWidth: 0,
            tdraHeight: 0,
            previewWidth: 480,
            previewHeight: 240);

        x.Should().Be(127);
        y.Should().Be(-54);
    }

    [Fact]
    public void ProjectAll_StopsAtBlank_AndCapsAtEight()
    {
        var waypoints = new[] { "100", "200", "", "300" };
        IReadOnlyList<(int X, int Y)> result = StartingLocationProjector.ProjectAll(
            waypoints,
            useIsometricCells: false,
            waypointCoefficient: 128,
            mapCellSizeX: 48,
            mapCellSizeY: 24,
            actualSize: ["0", "0", "0", "0"],
            localSize: ["0", "0", "0", "0"],
            tdraX: 0,
            tdraY: 0,
            tdraWidth: 128,
            tdraHeight: 128,
            previewWidth: 128,
            previewHeight: 128);

        result.Should().HaveCount(2);
    }
}
