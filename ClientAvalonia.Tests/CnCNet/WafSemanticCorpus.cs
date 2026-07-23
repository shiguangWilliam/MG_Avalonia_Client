using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Large programmatic WAF semantic corpus (mutations of keyword/regex seeds that
/// remain matchable after <c>WafTextNormalizer</c> NFKC / ZWSP / compact / confusable folds).
/// </summary>
internal static class WafSemanticCorpus
{
    public sealed record Case(string Id, string Surface /* "lobby"|"pm" */, string Text, bool ExpectWarn, string RuleHint);

    private static readonly IReadOnlyList<Case> Cached = BuildCore();

    public static IReadOnlyList<Case> Build() => Cached;

    public static IEnumerable<object[]> TheoryRows()
    {
        foreach (Case c in Build())
            yield return new object[] { c.Id, c.Surface, c.Text, c.ExpectWarn, c.RuleHint };
    }

    public static string InsertZwsp(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        var sb = new StringBuilder(s.Length * 2);
        for (int i = 0; i < s.Length; i++)
        {
            sb.Append(s[i]);
            if (i + 1 < s.Length)
                sb.Append('\u200b');
        }
        return sb.ToString();
    }

    public static string InsertSpaces(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        var sb = new StringBuilder(s.Length * 2);
        for (int i = 0; i < s.Length; i++)
        {
            sb.Append(s[i]);
            if (i + 1 < s.Length && !char.IsWhiteSpace(s[i]) && !char.IsWhiteSpace(s[i + 1]))
                sb.Append(' ');
        }
        return sb.ToString();
    }

    public static string InsertDots(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        var sb = new StringBuilder(s.Length * 2);
        for (int i = 0; i < s.Length; i++)
        {
            sb.Append(s[i]);
            if (i + 1 < s.Length && IsLetterOrDigitCompat(s[i]) && IsLetterOrDigitCompat(s[i + 1]))
                sb.Append('.');
        }
        return sb.ToString();
    }

    /// <summary>Map ASCII digit runs to fullwidth digits (NFKC folds them back).</summary>
    public static string WrapFullwidthDigits(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c is >= '0' and <= '9')
                sb.Append((char)('\uFF10' + (c - '0')));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsLetterOrDigitCompat(char c)
        => char.IsLetterOrDigit(c) || (c >= '\u4e00' && c <= '\u9fff');

    private static IReadOnlyList<Case> BuildCore()
    {
        var list = new List<Case>(280);

        // (idStem, text, ruleHint) — ExpectWarn=true
        (string Id, string Text, string Rule)[] warnSeeds =
        [
            // abuse / insult
            ("abuse.cn.nmsl", "nmsl", "content.abuse"),
            ("abuse.cn.cnmb", "cnmb 滚蛋", "content.abuse"),
            ("abuse.cn.wcnm", "wcnm", "content.abuse"),
            ("abuse.cn.sb", "你个sb", "content.abuse"),
            ("abuse.cn.shabi", "你这个傻逼", "content.abuse"),
            ("abuse.cn.nmsl.zh", "你妈死了", "content.abuse"),
            ("abuse.cn.caonima", "操你妈", "content.abuse"),
            ("abuse.cn.shabi.pinyin", "shabi", "content.abuse"),
            ("abuse.cn.sha_bi", "sha bi", "content.abuse"),
            ("abuse.en.fuckyou", "fuck you", "content.abuse"),
            ("abuse.en.asshole", "asshole", "content.abuse"),
            ("abuse.en.stfu", "stfu", "content.abuse"),
            ("abuse.en.kys", "kys", "content.abuse"),
            ("abuse.en.retard", "retard", "content.abuse"),
            ("abuse.cn.naocan", "脑残", "content.abuse"),
            ("abuse.cn.zhizhang", "智障", "content.abuse"),

            // promo
            ("promo.cn.dailian", "找代练", "content.promo"),
            ("promo.cn.peiwang", "陪玩低价", "content.promo"),
            ("promo.cn.studio", "工作室招人", "content.promo"),
            ("promo.cn.chongzhi", "扫码充值", "content.promo"),
            ("promo.cn.maihao", "卖号低价", "content.promo"),
            ("promo.pinyin.dailian", "dailian cheap", "content.promo"),
            ("promo.pinyin.peiwang", "peiwang now", "content.promo"),
            ("promo.pinyin.chongzhi", "chongzhi discount", "content.promo"),
            ("promo.pinyin.gongzuoshi", "gongzuoshi hiring", "content.promo"),
            ("promo.en.boosting", "cheap boosting", "content.promo"),
            ("promo.en.elo", "elo boost special offer", "content.promo"),
            ("promo.en.account", "buy account shop", "content.promo"),
            ("promo.cn.mianfei", "免费领优惠券", "content.promo"),

            // contact
            ("contact.cn.qqqun", "QQ群123456789", "content.contact"),
            ("contact.cn.qun", "q群 987654321", "content.contact"),
            ("contact.cn.weixin", "加我微信", "content.contact"),
            ("contact.cn.jiaqun", "加群领福利", "content.contact"),
            ("contact.pinyin.jiaqun", "jiaqun now", "content.contact"),
            ("contact.pinyin.weixin", "weixin add me", "content.contact"),
            ("contact.en.discord", "discord.gg/abcd123", "content.contact"),
            ("contact.en.telegram", "telegram t.me/boostshop", "content.contact"),
            ("contact.en.dmme", "dm me for coach", "content.contact"),
            ("contact.cn.xuanchuan", "宣传群：1122334455", "content.contact"),
            ("contact.cn.qunhao", "群号：556677889", "content.contact"),

            // fraud
            ("fraud.cn.yanzheng", "验证码发给我", "content.fraud"),
            ("fraud.cn.mima", "把密码告诉我", "content.fraud"),
            ("fraud.cn.zhuanzhang", "先转账再发货", "content.fraud"),
            ("fraud.cn.huikuan", "汇款到银行卡", "content.fraud"),
            ("fraud.en.seed", "send seed phrase", "content.fraud"),
            ("fraud.en.mnemonic", "mnemonic backup now", "content.fraud"),
            ("fraud.en.steam", "steam guard login code", "content.fraud"),
            ("fraud.en.phishing", "phishing password reset", "content.fraud"),
            ("fraud.cn.zhongjiang", "中奖领取奖金", "content.fraud"),

            // hate
            ("hate.cn.sima", "死妈玩意", "content.hate"),
            ("hate.cn.zhina", "支那滚", "content.hate"),
            ("hate.en.nigger", "nigger", "content.hate"),
            ("hate.en.hitler", "heil hitler", "content.hate"),
            ("hate.en.ching", "ching chong", "content.hate"),
            ("hate.en.goback", "go back to china", "content.hate"),

            // threat
            ("threat.cn.nongsi", "弄死你", "content.threat"),
            ("threat.cn.kansini", "砍死你", "content.threat"),
            ("threat.cn.dale", "打杀了你", "content.threat"),
            ("threat.en.kill", "i will kill you", "content.threat"),
            ("threat.en.shoot", "shoot you", "content.threat"),
            ("threat.en.swat", "swat you", "content.threat"),

            // sexual
            ("sexual.cn.yuepao", "约炮吗", "content.sexual"),
            ("sexual.cn.luoliao", "裸聊加我", "content.sexual"),
            ("sexual.cn.huang", "色情网站", "content.sexual"),
            ("sexual.en.porn", "free porn link", "content.sexual"),
            ("sexual.en.onlyfans", "onlyfans camgirl", "content.sexual"),
            ("sexual.en.sexchat", "sex chat now", "content.sexual"),

            // url-only
            ("url.http", "http://spam.vip/join", "content.url"),
            ("url.https", "https://ads.xyz/boost", "content.url"),
            ("url.www", "www.cheap-boost.com", "content.url"),
            ("url.cc", "visit boost.cc now", "content.url"),
            ("url.top", "open promo.top/x", "content.url"),

            // self_harm / child_safety (sparse but real keywords)
            ("selfharm.cn", "自杀教程分享", "content.self_harm"),
            ("selfharm.en", "how to kill yourself", "content.self_harm"),
            ("child.cn", "未成年约联系", "content.child_safety"),
            ("child.en", "underage sex trade", "content.child_safety"),
        ];

        foreach ((string id, string text, string rule) in warnSeeds)
            AddMutated(list, id, text, expectWarn: true, ruleHint: rule);

        // BENIGN allow (≥25) — avoid substrings that fold (e.g. "sb") or hit keywords
        string[] allowSeeds =
        [
            "gg wp",
            "你好",
            "大家好",
            "准备开战",
            "这张地图很好",
            "river raid next",
            "nice play",
            "well played",
            "see you next game",
            "我去吃饭了",
            "稍等一下",
            "换图吗",
            "标准对战开黑",
            "host please",
            "ready when you are",
            "地图名是什么",
            "ping ok",
            "tunnel fine",
            "gl hf",
            "good luck",
            "have fun",
            "再来一局",
            "我选苏联",
            "pick allied",
            "casual English chat",
            "thanks for the game",
            "今晚有空吗一起玩",
            "map talk only",
            "no rush please",
            "observer here",
        ];

        for (int i = 0; i < allowSeeds.Length; i++)
        {
            string stem = $"allow.{i:D2}";
            string text = allowSeeds[i];
            AddCase(list, stem + ".lobby", "lobby", text, false, "");
            AddCase(list, stem + ".pm", "pm", text, false, "");
            if (i % 3 == 0)
                AddCase(list, stem + ".zwsp.lobby", "lobby", InsertZwsp(text), false, "");
        }

        if (list.Count < 220)
            throw new InvalidOperationException($"WafSemanticCorpus expected >= 220 cases, got {list.Count}.");

        var dup = list.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
            throw new InvalidOperationException($"WafSemanticCorpus duplicate Id: {dup.Key}");

        return list;
    }

    private static void AddMutated(List<Case> list, string idStem, string text, bool expectWarn, string ruleHint)
    {
        // Surfaces alternate; mutations always include raw + zwsp + spaces.
        // Dots for letter/digit runs; fullwidth digits when digits present.
        (string Tag, string Text)[] variants =
        [
            ("raw", text),
            ("zwsp", InsertZwsp(text)),
            ("spc", InsertSpaces(text)),
            ("dot", InsertDots(text)),
        ];

        if (text.Any(char.IsDigit))
            variants = variants.Append(("fw", WrapFullwidthDigits(text))).ToArray();

        for (int i = 0; i < variants.Length; i++)
        {
            (string tag, string variantText) = variants[i];
            if (tag != "raw" && variantText == text)
                continue;

            // Prefer both surfaces on raw; mutate on alternating surface to keep corpus dense.
            if (tag == "raw")
            {
                AddCase(list, $"{idStem}.raw.lobby", "lobby", variantText, expectWarn, ruleHint);
                AddCase(list, $"{idStem}.raw.pm", "pm", variantText, expectWarn, ruleHint);
            }
            else
            {
                string surface = (i % 2 == 0) ? "lobby" : "pm";
                // URL regex needs scheme/host punctuation; space/dot collapse breaks it
                // and can falsely hit promo keywords (cheap/promo) after CompactForMatch.
                if (ruleHint == "content.url" && tag is "dot" or "spc")
                    continue;
                AddCase(list, $"{idStem}.{tag}.{surface}", surface, variantText, expectWarn, ruleHint);
            }
        }
    }

    private static void AddCase(List<Case> list, string id, string surface, string text, bool expectWarn, string ruleHint)
        => list.Add(new Case(id, surface, text, expectWarn, ruleHint));
}
