using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using ClientCore;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>
/// Builds / refreshes starting-location markers on a MapPreviewBox UiNodeViewModel.
/// </summary>
public static class MapPreviewOverlayApplier
{
    /// <summary>
    /// Phase 4 P4-3：Session-aware 主入口——直接吃 <see cref="IReadOnlyList{IPlayerSlot}"/>，
    /// 不再硬依赖 <see cref="LobbyPlayerState"/>。行为与旧入口完全等价。
    /// </summary>
    public static void Apply(
        UiNodeViewModel previewBox,
        MapEntry? map,
        IReadOnlyList<IPlayerSlot>? slots,
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
            var occupants = new List<IPlayerSlot>();
            if (slots != null)
            {
                foreach (IPlayerSlot slot in slots)
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

            // Phase 4 P4-3：MapStartLocationRules.CanJoinerSelect 已统一为 IPlayerSlot 签名（数组协变兼容）。
            bool selectable = canAssign
                || (canSelectLocal && MapStartLocationRules.CanJoinerSelect(
                    (IList<IPlayerSlot>?)slots ?? Array.Empty<IPlayerSlot>(),
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

    /// <summary>
    /// Legacy 入口（Phase 4 P4-3：标记为已过时）。新代码用 <see cref="Apply(UiNodeViewModel, MapEntry?, IReadOnlyList{IPlayerSlot}?, bool, bool)"/>。
    /// </summary>
    [Obsolete("Phase 4 P4-3: 改用 IReadOnlyList<IPlayerSlot> 重载。Phase 5 删除。")]
    public static void Apply(
        UiNodeViewModel previewBox,
        MapEntry? map,
        LobbyPlayerState? playerState,
        bool canAssign,
        bool canSelectLocal)
        => Apply(previewBox, map, playerState?.Slots, canAssign, canSelectLocal);

    private static string FormatOccupant(IPlayerSlot slot)
    {
        if (slot.TeamIndex > 0 && slot.TeamIndex <= ProgramConstants.TEAMS.Count)
            return $"[{ProgramConstants.TEAMS[slot.TeamIndex - 1]}] {slot.Name}";
        return slot.Name;
    }
}
