using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClientAvalonia.IniUi.Loading;

public enum IniBackgroundDrawMode
{
    Stretched,
    Centered,
    Tiled,
}

/// <summary>Maps INI DrawMode to Avalonia image/brush presentation (XNA PanelBackgroundImageDrawMode).</summary>
public static class IniDrawMode
{
    public static IniBackgroundDrawMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return IniBackgroundDrawMode.Stretched;

        return value.Trim().ToUpperInvariant() switch
        {
            "CENTERED" or "CENTER" => IniBackgroundDrawMode.Centered,
            "TILED" or "TILE" => IniBackgroundDrawMode.Tiled,
            _ => IniBackgroundDrawMode.Stretched,
        };
    }

    public static Stretch ToImageStretch(IniBackgroundDrawMode mode)
        => mode switch
        {
            IniBackgroundDrawMode.Centered => Stretch.Uniform,
            IniBackgroundDrawMode.Tiled => Stretch.None,
            _ => Stretch.Fill,
        };

    public static IBrush? CreateTiledBrush(Bitmap? bitmap)
    {
        if (bitmap == null)
            return null;

        return new ImageBrush(bitmap)
        {
            Stretch = Stretch.None,
            TileMode = TileMode.Tile,
            SourceRect = new RelativeRect(0, 0, 1, 1, RelativeUnit.Relative),
        };
    }
}
