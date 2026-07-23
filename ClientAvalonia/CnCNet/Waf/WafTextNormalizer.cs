using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ClientAvalonia.CnCNet.Waf;

public static class WafTextNormalizer
{
    private static readonly Regex IrcColor = new(@"\x03\d{0,2}(,\d{0,2})?", RegexOptions.Compiled);

    /// <summary>
    /// Light Latin↔Chinese promo confusables (not full pinyin NLP). Applied after NFKC.
    /// Longer keys first so "dai lian" wins over partials when present as spaced text.
    /// </summary>
    private static readonly (string From, string To)[] ConfusableFolds =
    [
        ("dai lian", "代练"),
        ("dailian", "代练"),
        ("pei wang", "陪玩"),
        ("peiwang", "陪玩"),
        ("chong zhi", "充值"),
        ("chongzhi", "充值"),
        ("gongzuoshi", "工作室"),
        ("maihao", "卖号"),
        ("shouhao", "收号"),
        ("jiagun", "加群"),
        ("jiaqun", "加群"),
        ("weixin", "微信"),
        ("sha bi", "傻逼"),
        ("shabi", "傻逼"),
        ("cao ni ma", "操你妈"),
        ("caonima", "操你妈"),
        ("nmsl", "你妈死了"),
        ("cnmb", "操你妈"),
        ("wcnm", "我操你妈"),
    ];

    public static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        string s = IrcColor.Replace(input, string.Empty);
        s = s.Replace("\u0002", string.Empty)
            .Replace("\u001f", string.Empty)
            .Replace("\u0016", string.Empty)
            .Replace("\u000f", string.Empty)
            .Replace("\u0001", string.Empty);

        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Normalize(NormalizationForm.FormKC))
        {
            if (c is '\u200b' or '\u200c' or '\u200d' or '\ufeff'
                or '\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e'
                or '\u2066' or '\u2067' or '\u2068' or '\u2069')
            {
                continue;
            }

            sb.Append(c);
        }

        return FoldConfusables(sb.ToString().Trim());
    }

    /// <summary>
    /// Fold a small set of Latin phonetic / acronym spam forms into Chinese keywords
    /// so existing keyword/regex corpora still match.
    /// </summary>
    public static string FoldConfusables(string normalized)
    {
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        string s = normalized;
        foreach ((string from, string to) in ConfusableFolds)
        {
            if (from.Length == 0)
                continue;
            s = Regex.Replace(s, Regex.Escape(from), to, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return s;
    }

    /// <summary>
    /// Collapse whitespace/punctuation so spaced / punctuated keyword variants still match
    /// (e.g. "代 练", "加-群", fullwidth forms already folded by NFKC in <see cref="Normalize"/>).
    /// </summary>
    public static string CompactForMatch(string normalized)
    {
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        var sb = new StringBuilder(normalized.Length);
        foreach (char c in normalized.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c) || c is '_' or '-' or '*' or '.')
                continue;
            sb.Append(c);
        }

        // Second pass: acronyms that only appear after punctuation collapse (n.m.s.l → nmsl).
        string compact = sb.ToString();
        return FoldConfusables(compact);
    }
}
