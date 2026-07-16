using Avalonia.Media;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// Parses IRC color-prefixed PRIVMSG bodies the same way DX
/// <c>CnCNetManager.DoChatMessageReceived</c> does.
/// </summary>
public static class CnCNetIrcChatText
{
    public static Color DefaultChatColor { get; } = Colors.White;

    public static Color SystemNoticeColor { get; } = Colors.Silver;

    /// <summary>
    /// Strip the leading <c>\x03NN</c> color prefix (if present) and resolve the display color.
    /// Color indices mirror <see cref="CnCNetChatColorCatalog"/> (and DX ircChatColors).
    /// Indices 0/1 (config-dependent defaults) fall back to <paramref name="fallbackColor"/>.
    /// </summary>
    public static (string Text, Color Color) Parse(string message, Color fallbackColor)
    {
        if (string.IsNullOrEmpty(message))
            return (string.Empty, fallbackColor);

        Color color = fallbackColor;
        if (message.Contains('\u0003'))
        {
            if (message.Length >= 3)
            {
                string colorString = message.Substring(1, 2);
                message = message.Length > 3 ? message[3..] : string.Empty;
                if (int.TryParse(colorString, out int colorIndex))
                    color = ColorForIrcId(colorIndex, fallbackColor);
            }
        }

        if (message.Length > 0 && message[^1] == '\u001f')
            message = message[..^1];

        return (message.Replace('\r', ' ').Trim(), color);
    }

    /// <summary>
    /// Fixed display colors for IRC ids 2–15 (matches <see cref="CnCNetChatColorCatalog"/>).
    /// Ids 0–1 are personal default colors from ClientConfiguration — use
    /// <paramref name="fallback"/> instead so parsers stay config-free.
    /// </summary>
    public static Color ColorForIrcId(int ircColorId, Color fallback)
        => ircColorId switch
        {
            2 => Colors.LightBlue,
            3 => Colors.ForestGreen,
            4 => Color.FromRgb(180, 0, 0),
            5 => Colors.Red,
            6 => Colors.MediumPurple,
            7 => Colors.Orange,
            8 => Colors.Yellow,
            9 => Colors.LimeGreen,
            10 => Colors.Turquoise,
            11 => Colors.LightSkyBlue,
            12 => Colors.RoyalBlue,
            13 => Colors.DeepPink,
            14 => Colors.LightGray,
            15 => Colors.Gray,
            _ => fallback,
        };

    public static Color ResolveSelectedChatColor(int catalogIndex)
        => CnCNetChatColorCatalog.GetEntry(catalogIndex).DisplayColor;
}
