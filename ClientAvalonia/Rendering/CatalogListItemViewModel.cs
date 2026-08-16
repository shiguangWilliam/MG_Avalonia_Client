using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClientAvalonia.Rendering;

public sealed class CatalogListItemViewModel
{
    public required string Text { get; init; }

    public Bitmap? Icon { get; init; }

    /// <summary>Optional per-item foreground (e.g. CnCNet chat line color). Null = template default.</summary>
    public IBrush? ForegroundBrush { get; init; }

    public bool HasCustomForeground => ForegroundBrush != null;

    public bool IsHeader { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsSelectable => IsEnabled && !IsHeader;

    public bool HasIcon => Icon != null;

    public string? ToolTip { get; init; }

    /// <summary>Optional Tactical-globe latitude in degrees. Null = no fixed position.</summary>
    public double? GlobeLatitude { get; init; }

    /// <summary>Optional Tactical-globe longitude in degrees. Null = no fixed position.</summary>
    public double? GlobeLongitude { get; init; }

    public bool HasGlobePosition => GlobeLatitude.HasValue && GlobeLongitude.HasValue;

    public double Opacity => IsHeader || IsEnabled ? 1.0 : 0.55;
}
