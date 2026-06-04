namespace ClientAvalonia.IniUi.Layout;

/// <summary>Viewport for layout expressions; typically matches MainMenu.ini Size or user client resolution.</summary>
public sealed class LayoutContext
{
    public static LayoutContext M2Default { get; } = new(1280, 720, DefaultParserConstants.Create());

    public LayoutContext(int width, int height, IReadOnlyDictionary<string, int>? parserConstants = null)
    {
        Width = width;
        Height = height;
        ParserConstants = parserConstants ?? DefaultParserConstants.Create();
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyDictionary<string, int> ParserConstants { get; }
}
