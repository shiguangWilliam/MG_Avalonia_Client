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
    /// B2: characters stripped from incoming chat text before rendering. They have
    /// no legitimate use in lobby chat and can be used for visual deception
    /// (RTL/LTR override attacks), display corruption (NUL), or invisible-string
    /// tricks (zero-width). IRC color (\u0003), bold (\u0002), italic (\u001d),
    /// underline (\u001f), and reverse (\u0016) are also dropped because the
    /// Avalonia TextBlock path doesn't honor them.
    /// </summary>
    private static readonly char[] _sanitizedControlChars =
    {
        '\u0000', // NUL
        '\u0002', // bold
        '\u0003', // color (already consumed by Parse, but kept for safety)
        '\u0007', // BEL
        '\u0008', // backspace
        '\u000B', // vertical tab
        '\u000C', // form feed
        '\u000E', // SO  (shift out)
        '\u000F', // SI  (shift in)
        '\u0010', // DLE
        '\u0011', // DC1 (XON)
        '\u0012', // DC2
        '\u0013', // DC3 (XOFF)
        '\u0014', // DC4
        '\u0015', // NAK
        '\u0016', // SYN (reverse video / IRC swap)
        '\u0017', // ETB
        '\u0018', // CAN
        '\u0019', // EM
        '\u001A', // SUB
        '\u001B', // ESC
        '\u001C', // FS
        '\u001D', // GS  (italic)
        '\u001E', // RS
        '\u001F', // US  (underline)
        '\u007F', // DEL
        '\u200B', // zero-width space
        '\u200C', // zero-width non-joiner
        '\u200D', // zero-width joiner
        '\u200E', // LRM
        '\u200F', // RLM
        '\u202A', // LRE — Left-To-Right Embedding (override attack)
        '\u202B', // RLE — Right-To-Left Embedding (override attack)
        '\u202C', // PDF — Pop Directional Formatting
        '\u202D', // LRO — Left-To-Right Override (attack)
        '\u202E', // RLO — Right-To-Left Override (attack)
        '\u2066', // LRI
        '\u2067', // RLI
        '\u2068', // FSI
        '\u2069', // PDI
        '\uFEFF', // ZWNBSP / BOM
    };

    /// <summary>
    /// DX ACTION CTCP arrives as PRIVMSG with illegal SOH (<c>\u0001</c>) delimiters on
    /// <em>both</em> sides: <c>\u0001ACTION text\u0001</c>. DX Connection.cs strips them with a
    /// length-arithmetic that throws on bare <c>\u0001ACTION\u0001</c>, then
    /// <c>DoChatMessageReceived</c> does <c>Remove(0, 7)</c> assuming an <c>ACTION </c> prefix —
    /// that is the KABOOOOOOM path. We strip the flanking SOHs first, then take the body
    /// without fixed-length removes.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <paramref name="raw"/> is an ACTION CTCP (after SOH strip);
    /// <paramref name="actionBody"/> is the text after the <c>ACTION</c> token (may be empty).
    /// </returns>
    public static bool TryNormalizeActionCtcp(string? raw, out string actionBody)
    {
        actionBody = string.Empty;
        if (string.IsNullOrEmpty(raw))
            return false;

        // Must be CTCP-framed (illegal SOH present). Plain chat containing the word
        // "ACTION" must not be rewritten.
        if (raw.IndexOf('\u0001') < 0)
            return false;

        // Illegal CTCP delimiters may sit on either/both ends — Trim them before any indexing.
        string inner = raw.Trim('\u0001');
        if (!inner.StartsWith("ACTION", StringComparison.Ordinal))
            return false;

        if (inner.Length == 6)
            return true; // bare ACTION, empty body

        // Prefer "ACTION <body>"; if the space is missing, take whatever follows "ACTION".
        if (inner[6] == ' ')
        {
            actionBody = inner.Length > 7 ? inner[7..] : string.Empty;
            return true;
        }

        actionBody = inner[6..];
        return true;
    }

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

        // B2: collapse stray CR and strip all dangerous control / bidi-override
        // characters before handing the text to the renderer.
        message = SanitizeDisplayText(message.Replace('\r', ' ')).Trim();

        return (message, color);
    }

    /// <summary>
    /// B2: remove IRC / Unicode control characters that have no legitimate place
    /// in lobby chat and can be used to spoof, hide, or corrupt visible text.
    /// Plain whitespace is preserved. Newlines are preserved (chat already
    /// formats them per-line). Tabs are preserved (legitimate formatting).
    /// </summary>
    public static string SanitizeDisplayText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Fast path: if no chars need stripping, return as-is.
        bool needsSanitizing = false;
        foreach (char c in text)
        {
            if (IsSanitizedChar(c))
            {
                needsSanitizing = true;
                break;
            }
        }

        if (!needsSanitizing)
            return text;

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (!IsSanitizedChar(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsSanitizedChar(char c)
    {
        // C0 control chars except \t (\u0009), \n (\u000A), \r (\u000D).
        // We let \r through (callers normalize it to space first); we keep
        // \n so multi-line chat stays multi-line.
        if (c < '\u0020' && c != '\t' && c != '\n' && c != '\r')
            return true;

        // DEL + the explicit unsafe list above.
        if (c == '\u007F')
            return true;

        switch (c)
        {
            case '\u200B':
            case '\u200C':
            case '\u200D':
            case '\u200E':
            case '\u200F':
            case '\u202A':
            case '\u202B':
            case '\u202C':
            case '\u202D':
            case '\u202E':
            case '\u2066':
            case '\u2067':
            case '\u2068':
            case '\u2069':
            case '\uFEFF':
                return true;
            default:
                return false;
        }
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
