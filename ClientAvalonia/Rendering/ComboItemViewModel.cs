using Avalonia.Media.Imaging;

namespace ClientAvalonia.Rendering;

public sealed class ComboItemViewModel
{
    public required string Text { get; init; }

    public Bitmap? Icon { get; init; }

    public string? Tag { get; init; }

    public bool HasIcon => Icon != null;
}
