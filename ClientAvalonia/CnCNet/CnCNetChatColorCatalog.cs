using Avalonia.Media;
using ClientCore;
using ClientCore.Extensions;
using ClientCore.Settings;

namespace ClientAvalonia.CnCNet;

/// <summary>IRC chat colors (XNA CnCNetManager.ircChatColors).</summary>
public sealed class CnCNetChatColorEntry
{
    public required string Name { get; init; }

    public required int IrcColorId { get; init; }

    public required Color DisplayColor { get; init; }

    public bool Selectable { get; init; } = true;
}

public static class CnCNetChatColorCatalog
{
    private static IReadOnlyList<CnCNetChatColorEntry>? _cached;

    public static IReadOnlyList<CnCNetChatColorEntry> LoadSelectable()
        => LoadAll().Where(c => c.Selectable).ToList();

    public static IReadOnlyList<CnCNetChatColorEntry> LoadAll()
    {
        if (_cached != null)
            return _cached;

        Color defaultColor = ParseRgb(ClientConfiguration.Instance.DefaultChatColor, Colors.LimeGreen);
        _cached =
        [
            new() { Name = "Default color".L10N("Client:Main:ColorDefault"), IrcColorId = 0, DisplayColor = defaultColor, Selectable = false },
            new() { Name = "Default color #2".L10N("Client:Main:ColorDefault2"), IrcColorId = 1, DisplayColor = defaultColor, Selectable = false },
            new() { Name = "Light Blue".L10N("Client:Main:ColorLightBlue"), IrcColorId = 2, DisplayColor = Colors.LightBlue },
            new() { Name = "Green".L10N("Client:Main:ColorGreen"), IrcColorId = 3, DisplayColor = Colors.ForestGreen },
            new() { Name = "Dark Red".L10N("Client:Main:ColorDarkRed"), IrcColorId = 4, DisplayColor = Color.FromRgb(180, 0, 0) },
            new() { Name = "Red".L10N("Client:Main:ColorRed"), IrcColorId = 5, DisplayColor = Colors.Red },
            new() { Name = "Purple".L10N("Client:Main:ColorPurple"), IrcColorId = 6, DisplayColor = Colors.MediumPurple },
            new() { Name = "Orange".L10N("Client:Main:ColorOrange"), IrcColorId = 7, DisplayColor = Colors.Orange },
            new() { Name = "Yellow".L10N("Client:Main:ColorYellow"), IrcColorId = 8, DisplayColor = Colors.Yellow },
            new() { Name = "Lime Green".L10N("Client:Main:ColorLimeGreen"), IrcColorId = 9, DisplayColor = Colors.LimeGreen },
            new() { Name = "Turquoise".L10N("Client:Main:ColorTurquoise"), IrcColorId = 10, DisplayColor = Colors.Turquoise },
            new() { Name = "Sky Blue".L10N("Client:Main:ColorSkyBlue"), IrcColorId = 11, DisplayColor = Colors.LightSkyBlue },
            new() { Name = "Blue".L10N("Client:Main:ColorBlue"), IrcColorId = 12, DisplayColor = Colors.RoyalBlue },
            new() { Name = "Pink".L10N("Client:Main:ColorPink"), IrcColorId = 13, DisplayColor = Colors.DeepPink },
            new() { Name = "Metalic".L10N("Client:Main:ColorLightGrayMetalic"), IrcColorId = 14, DisplayColor = Colors.LightGray },
            new() { Name = "Gray".L10N("Client:Main:ColorGray"), IrcColorId = 15, DisplayColor = Colors.Gray, Selectable = false },
        ];
        return _cached;
    }

    public static int ResolveSelectedIndex(int savedIndex)
    {
        IReadOnlyList<CnCNetChatColorEntry> all = LoadAll();
        if (savedIndex >= 0 && savedIndex < all.Count)
            return savedIndex;

        int fallback = ClientConfiguration.Instance.DefaultPersonalChatColorIndex;
        return fallback >= 0 && fallback < all.Count ? fallback : 3;
    }

    public static CnCNetChatColorEntry GetEntry(int index)
    {
        IReadOnlyList<CnCNetChatColorEntry> all = LoadAll();
        if (index < 0 || index >= all.Count)
            index = ResolveSelectedIndex(UserINISettings.Instance.ChatColor);

        return all[index];
    }

    private static Color ParseRgb(string raw, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return fallback;

        if (!byte.TryParse(parts[0], out byte r) || !byte.TryParse(parts[1], out byte g) || !byte.TryParse(parts[2], out byte b))
            return fallback;

        return Color.FromRgb(r, g, b);
    }
}
