using Avalonia.Media.Imaging;

namespace ClientAvalonia.Rendering;

public sealed class CatalogListItemViewModel
{
    public required string Text { get; init; }

    public Bitmap? Icon { get; init; }

    public bool IsHeader { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsSelectable => IsEnabled && !IsHeader;

    public bool HasIcon => Icon != null;
}
