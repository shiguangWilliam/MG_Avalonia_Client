using System;
using System.Collections.Generic;
using System.Globalization;

namespace ClientAvalonia.Domain;

/// <summary>
/// Projects raw waypoint tokens onto map-preview pixel coordinates.
/// Port of DXMainClient <c>Map.GetTDRAWaypointCoords</c> /
/// <c>Map.GetIsometricWaypointCoords</c> — pure math, no UI dependencies.
/// </summary>
public static class StartingLocationProjector
{
    public const int MaxPlayers = 8;

    /// <summary>
    /// Projects a single waypoint string to preview-image pixel coordinates.
    /// </summary>
    /// <param name="waypoint">
    /// Isometric: <c>"YXX,level"</c> where XX is the last 3 digits of the packed tile
    /// (DX convention). TDRA: a single integer packing <c>Y * coefficient + X</c>.
    /// </param>
    public static (int X, int Y) Project(
        string waypoint,
        bool useIsometricCells,
        int waypointCoefficient,
        int mapCellSizeX,
        int mapCellSizeY,
        IReadOnlyList<string> actualSize,
        IReadOnlyList<string> localSize,
        int tdraX,
        int tdraY,
        int tdraWidth,
        int tdraHeight,
        int previewWidth,
        int previewHeight)
    {
        if (string.IsNullOrWhiteSpace(waypoint) || previewWidth <= 0 || previewHeight <= 0)
            return (0, 0);

        if (useIsometricCells)
            return ProjectIsometric(waypoint, actualSize, localSize, mapCellSizeX, mapCellSizeY, previewWidth, previewHeight);

        return ProjectTdra(waypoint, waypointCoefficient, tdraX, tdraY, tdraWidth, tdraHeight, previewWidth, previewHeight);
    }

    /// <summary>Projects every waypoint, stopping at the first blank (DX order).</summary>
    public static IReadOnlyList<(int X, int Y)> ProjectAll(
        IReadOnlyList<string> waypoints,
        bool useIsometricCells,
        int waypointCoefficient,
        int mapCellSizeX,
        int mapCellSizeY,
        IReadOnlyList<string> actualSize,
        IReadOnlyList<string> localSize,
        int tdraX,
        int tdraY,
        int tdraWidth,
        int tdraHeight,
        int previewWidth,
        int previewHeight)
    {
        if (waypoints.Count == 0)
            return [];

        var result = new List<(int X, int Y)>(Math.Min(waypoints.Count, MaxPlayers));
        foreach (string waypoint in waypoints)
        {
            if (string.IsNullOrWhiteSpace(waypoint))
                break;

            result.Add(Project(
                waypoint,
                useIsometricCells,
                waypointCoefficient,
                mapCellSizeX,
                mapCellSizeY,
                actualSize,
                localSize,
                tdraX,
                tdraY,
                tdraWidth,
                tdraHeight,
                previewWidth,
                previewHeight));

            if (result.Count >= MaxPlayers)
                break;
        }

        return result;
    }

    private static (int X, int Y) ProjectTdra(
        string waypoint,
        int waypointCoefficient,
        int x,
        int y,
        int width,
        int height,
        int previewWidth,
        int previewHeight)
    {
        if (!int.TryParse(waypoint.Split(',')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int packed)
            || packed < 0
            || width <= 0
            || height <= 0
            || waypointCoefficient <= 0)
        {
            return (0, 0);
        }

        int cellX = packed % waypointCoefficient;
        int cellY = packed / waypointCoefficient;
        double ratioX = (cellX - x) / (double)width;
        double ratioY = (cellY - y) / (double)height;
        return ((int)(ratioX * previewWidth), (int)(ratioY * previewHeight));
    }

    private static (int X, int Y) ProjectIsometric(
        string waypoint,
        IReadOnlyList<string> actualSize,
        IReadOnlyList<string> localSize,
        int cellSizeX,
        int cellSizeY,
        int previewWidth,
        int previewHeight)
    {
        if (actualSize.Count < 4 || localSize.Count < 4)
            return (0, 0);

        string[] parts = waypoint.Split(',');
        string packed = parts[0];
        if (packed.Length < 4)
            return (0, 0);

        int xCoordIndex = packed.Length - 3;
        if (!int.TryParse(packed.AsSpan(0, xCoordIndex), NumberStyles.Integer, CultureInfo.InvariantCulture, out int isoTileY)
            || !int.TryParse(packed.AsSpan(xCoordIndex), NumberStyles.Integer, CultureInfo.InvariantCulture, out int isoTileX))
        {
            return (0, 0);
        }

        int level = 0;
        if (parts.Length > 1)
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out level);

        if (!TryParseInt(actualSize[2], out int actualSizeW)
            || !TryParseInt(localSize[0], out int localX)
            || !TryParseInt(localSize[1], out int localY)
            || !TryParseInt(localSize[2], out int localW)
            || !TryParseInt(localSize[3], out int localH))
        {
            return (0, 0);
        }

        int rx = isoTileX - isoTileY + actualSizeW - 1;
        int ry = isoTileX + isoTileY - actualSizeW - 1;

        int pixelPosX = rx * cellSizeX / 2;
        int pixelPosY = ry * cellSizeY / 2 - level * cellSizeY / 2;
        pixelPosX -= localX * cellSizeX;
        pixelPosY -= localY * cellSizeY;

        int mapSizeX = localW * cellSizeX;
        int mapSizeY = localH * cellSizeY;
        if (mapSizeX <= 0 || mapSizeY <= 0)
            return (0, 0);

        double ratioX = pixelPosX / (double)mapSizeX;
        double ratioY = pixelPosY / (double)mapSizeY;
        return ((int)(ratioX * previewWidth), (int)(ratioY * previewHeight));
    }

    private static bool TryParseInt(string value, out int result)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}
