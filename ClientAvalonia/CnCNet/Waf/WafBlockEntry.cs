using System;
using System.Text.Json.Serialization;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>
/// One player-confirmed WAF blocklist row. Match key is <see cref="Key"/> (e.g. <c>nick=Alice</c>);
/// optional IRC actor triple <c>nick!ident@host</c> is shown in Settings → Security.
/// </summary>
public sealed class WafBlockEntry
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("nick")]
    public string Nick { get; set; } = string.Empty;

    [JsonPropertyName("ident")]
    public string Ident { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonPropertyName("addedUtc")]
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

    public string ActorTriple
    {
        get
        {
            string nick = string.IsNullOrWhiteSpace(Nick) ? "-" : Nick.Trim();
            string ident = string.IsNullOrWhiteSpace(Ident) ? "-" : Ident.Trim();
            string host = string.IsNullOrWhiteSpace(Host) ? "-" : Host.Trim();
            return $"{nick}!{ident}@{host}";
        }
    }

    public string DisplayLine
    {
        get
        {
            string kind = string.IsNullOrWhiteSpace(Kind) ? InferKind(Key) : Kind;
            string target = ExtractTarget(Key);
            string note = string.IsNullOrWhiteSpace(Note) ? string.Empty : $" · {Note.Trim()}";
            string when = AddedUtc == default ? string.Empty : $" · {AddedUtc:yyyy-MM-dd HH:mm}Z";
            return $"[{kind}] {target} · {ActorTriple}{note}{when}";
        }
    }

    public static WafBlockEntry FromKey(
        string blockKey,
        string? nick = null,
        string? ident = null,
        string? host = null,
        string? note = null)
    {
        string key = (blockKey ?? string.Empty).Trim();
        string kind = InferKind(key);
        string inferredNick = nick ?? string.Empty;
        if (string.IsNullOrWhiteSpace(inferredNick)
            && kind.Equals("nick", StringComparison.OrdinalIgnoreCase))
        {
            inferredNick = ExtractTarget(key);
        }

        return new WafBlockEntry
        {
            Key = key,
            Kind = kind,
            Nick = inferredNick.Trim(),
            Ident = (ident ?? string.Empty).Trim(),
            Host = (host ?? string.Empty).Trim(),
            Note = (note ?? string.Empty).Trim(),
            AddedUtc = DateTime.UtcNow,
        };
    }

    public static string InferKind(string key)
    {
        int eq = key.IndexOf('=');
        if (eq <= 0)
            return "manual";
        return key[..eq].Trim().ToLowerInvariant();
    }

    public static string ExtractTarget(string key)
    {
        int eq = key.IndexOf('=');
        if (eq < 0 || eq >= key.Length - 1)
            return key;
        return key[(eq + 1)..].Trim();
    }

    /// <summary>Normalize user input from the Security add box into a match key.</summary>
    public static string NormalizeManualKey(string raw)
    {
        string t = (raw ?? string.Empty).Trim();
        if (t.Length == 0)
            return string.Empty;

        if (t.Contains('=', StringComparison.Ordinal))
            return t;

        if (t.StartsWith('#'))
            return "room=" + t;

        if (t.Contains(':') && t.Split(':').Length == 2
            && ushort.TryParse(t.Split(':')[1], out _))
            return "tunnel=" + t;

        return "nick=" + t;
    }
}
