using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClientAvalonia.Rendering;

public sealed class ComboItemViewModel
{
    public required string Text { get; init; }

    public Bitmap? Icon { get; init; }

    public IBrush? SwatchBrush { get; init; }

    public string? Tag { get; init; }

    public bool HasIcon => Icon != null;

    public bool HasSwatch => SwatchBrush != null;
}
