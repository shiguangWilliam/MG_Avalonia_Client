using System;

namespace ClientAvalonia.CnCNet;

/// <summary>Parse IRC source prefixes of the form <c>nick!ident@host</c>.</summary>
public static class IrcUserPrefix
{
    public static bool TryParse(string? prefix, out string nick, out string ident, out string host)
    {
        nick = string.Empty;
        ident = string.Empty;
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(prefix))
            return false;

        string p = prefix.Trim();
        int bang = p.IndexOf('!');
        if (bang <= 0)
            return false;

        nick = p[..bang];
        string rest = p[(bang + 1)..];
        int at = rest.IndexOf('@');
        if (at < 0)
        {
            ident = rest;
            return nick.Length > 0;
        }

        ident = rest[..at];
        host = rest[(at + 1)..];
        return nick.Length > 0;
    }
}
