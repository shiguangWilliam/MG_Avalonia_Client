using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientCore;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>
/// Builds / refreshes starting-location markers on a MapPreviewBox UiNodeViewModel.
/// </summary>
public static class MapPreviewOverlayApplier
{
    public static void Apply(
        UiNodeViewModel previewBox,
        MapEntry? map,
        LobbyPlayerState? playerState,
        bool canAssign,
        bool canSelectLocal)
    {
        if (map == null || map.Waypoints.Count == 0 || previewBox.PreviewImage == null)
        {
            previewBox.SetStartMarkers([]);
            previewBox.MapPreviewCanAssign = false;
            previewBox.MapPreviewCanSelectLocal = false;
            return;
        }

        Bitmap preview = previewBox.PreviewImage;
        int previewW = preview.PixelSize.Width;
        int previewH = preview.PixelSize.Height;
        int controlW = (int)Math.Max(1, previewBox.Width);
        int controlH = (int)Math.Max(1, previewBox.Height);

        ClientConfiguration cfg = ClientConfiguration.Instance;
        IReadOnlyList<(int X, int Y)> projected = StartingLocationProjector.ProjectAll(
            map.Waypoints,
            cfg.UseIsometricCells,
            cfg.WaypointCoefficient,
            cfg.MapCellSizeX,
            cfg.MapCellSizeY,
            map.ActualSize,
            map.LocalSize,
            map.MapX,
            map.MapY,
            map.MapWidth,
            map.MapHeight,
            previewW,
            previewH);

        MapPreviewOverlayGeometry.LetterboxLayout layout =
            MapPreviewOverlayGeometry.ComputeLetterbox(controlW, controlH, previewW, previewH);

        int visibleCount = projected.Count;
        if (map.MaxPlayers > 0)
            visibleCount = Math.Min(visibleCount, map.MaxPlayers);

        IReadOnlyList<MultiplayerColorCatalog.MultiplayerColorEntry> colors = MultiplayerColorCatalog.Load();
        var markers = new List<MapStartMarkerVm>(visibleCount);

        for (int i = 0; i < visibleCount; i++)
        {
            MapPreviewOverlayGeometry.IndicatorBounds bounds =
                MapPreviewOverlayGeometry.ProjectIndicator(layout, projected[i].X, projected[i].Y);

            int start1Based = i + 1;
            var occupants = new List<LobbyPlayerSlot>();
            if (playerState != null)
            {
                foreach (LobbyPlayerSlot slot in playerState.Slots)
                {
                    if (slot.IsOccupied && slot.StartIndex == start1Based)
                        occupants.Add(slot);
                }
            }

            IBrush fill = Brushes.WhiteSmoke;
            IBrush ring = new SolidColorBrush(Color.FromRgb(180, 180, 180));
            if (occupants.Count == 1 && occupants[0].ColorIndex > 0 && occupants[0].ColorIndex <= colors.Count)
            {
                MultiplayerColorCatalog.MultiplayerColorEntry c = colors[occupants[0].ColorIndex - 1];
                fill = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                ring = fill;
            }
            else if (occupants.Count > 0)
            {
                fill = new SolidColorBrush(Color.FromRgb(255, 200, 80));
                ring = fill;
            }

            string occupantText = string.Join(
                Environment.NewLine,
                occupants.Select(FormatOccupant));

            bool selectable = canAssign
                || (canSelectLocal && MapStartLocationRules.CanJoinerSelect(
                    playerState?.Slots ?? (IList<LobbyPlayerSlot>)Array.Empty<LobbyPlayerSlot>(),
                    start1Based,
                    map.EnforceMaxPlayers));

            markers.Add(new MapStartMarkerVm
            {
                Index = start1Based,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Label = start1Based.ToString(),
                IsOccupied = occupants.Count > 0,
                IsSelectable = selectable,
                OccupantText = occupantText,
                FillBrush = fill,
                RingBrush = ring,
            });
        }

        previewBox.SetStartMarkers(markers);
        previewBox.MapPreviewCanAssign = canAssign;
        previewBox.MapPreviewCanSelectLocal = canSelectLocal;
        previewBox.MapPreviewEnforceMaxPlayers = map.EnforceMaxPlayers;
    }

    private static string FormatOccupant(LobbyPlayerSlot slot)
    {
        if (slot.TeamIndex > 0 && slot.TeamIndex <= ProgramConstants.TEAMS.Count)
            return $"[{ProgramConstants.TEAMS[slot.TeamIndex - 1]}] {slot.Name}";
        return slot.Name;
    }
}
