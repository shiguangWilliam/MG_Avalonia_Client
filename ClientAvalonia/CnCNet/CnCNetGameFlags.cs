using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

/// <summary>Parses GAME CTCP flags field (XNA CnCNetLobby / DXMain CnCNetLobby).</summary>
public static class CnCNetGameFlags
{
    public const int ExpectedLength = 5;

    /// <summary>DX GAME flags index 1 (isCustomPassword). Only <c>1</c> means passworded.</summary>
    public static bool ParsePassworded(string? flags)
    {
        flags = Normalize(flags);
        return flags[1] == '1';
    }

    public static string Normalize(string? flags)
    {
        if (string.IsNullOrEmpty(flags))
            return new string('0', ExpectedLength);

        return flags.Length >= ExpectedLength
            ? flags[..ExpectedLength]
            : flags.PadRight(ExpectedLength, '0');
    }

    public static bool ParseLocked(string? flags)
        => ParseFlag(flags, 0, defaultValue: true);

    public static bool ParseClosed(string? flags)
        => ParseFlag(flags, 2, defaultValue: true);

    public static bool ParseLoadedGame(string? flags)
        => ParseFlag(flags, 3, defaultValue: false);

    public static bool ParseLadder(string? flags)
        => ParseFlag(flags, 4, defaultValue: false);

    /// <summary>Builds the 5-char flags string (locked, passworded, closed, loaded, ladder).</summary>
    public static string Build(bool locked, bool passworded, bool closed, bool loadedGame = false, bool ladder = false)
        => (locked ? "1" : "0")
           + (passworded ? "1" : "0")
           + (closed ? "1" : "0")
           + (loadedGame ? "1" : "0")
           + (ladder ? "1" : "0");

    /// <summary>GSETTINGS password flag: only integer 1 means passworded (DX isCustomPassword).</summary>
    public static bool ParseSettingsPassworded(string? value)
        => Conversions.IntFromString(value, 0) == 1;

    private static bool ParseFlag(string? flags, int index, bool defaultValue)
    {
        if (string.IsNullOrEmpty(flags) || flags.Length <= index)
            return defaultValue;

        return Conversions.BooleanFromString(flags.Substring(index, 1), defaultValue);
    }
}
