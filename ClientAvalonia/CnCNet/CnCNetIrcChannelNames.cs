namespace ClientAvalonia.CnCNet;

/// <summary>IRC channel name helpers aligned with DXMain (preserve create/join casing).</summary>
internal static class CnCNetIrcChannelNames
{
    /// <summary>Ensure leading # without changing case (GAME payload, JOIN, MODE/TOPIC).</summary>
    public static string Preserve(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return string.Empty;

        string normalized = channel.Trim();
        if (!normalized.StartsWith('#'))
            normalized = "#" + normalized;

        return normalized;
    }

    /// <summary>Lower-case channel for comparisons only (maps, membership keys).</summary>
    public static string Normalize(string channel)
    {
        string preserved = Preserve(channel);
        return string.IsNullOrEmpty(preserved) ? string.Empty : preserved.ToLowerInvariant();
    }
}
