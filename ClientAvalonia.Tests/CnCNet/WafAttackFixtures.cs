using System;
using System.Collections.Generic;
using ClientAvalonia.CnCNet.Waf;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>Synthetic hostile traffic used by WAF MVP attack scenarios (hang-room bot / promo spam).</summary>
internal static class WafAttackFixtures
{
    public const string HostBotTunnelHost = "175.178.174.40";
    public const ushort HostBotTunnelPort = 50000;

    public static WafGameBroadcastFields HostBotGame(
        string nick = "BotHostA",
        string channel = "#ym-bot-001",
        string roomName = "免费代练房",
        string revision = "R8",
        int fieldCount = 9,
        IReadOnlyList<string>? players = null)
        => new()
        {
            Revision = revision,
            FieldCount = fieldCount,
            ChannelName = channel,
            RoomName = roomName,
            MapName = "Standard Map",
            GameMode = "标准对战",
            TunnelHost = HostBotTunnelHost,
            TunnelPort = HostBotTunnelPort,
            Players = players ?? ["A", "B", "C", "D", "E"],
        };

    public static WafIngressEvent HostBotBroadcast(string nick, WafGameBroadcastFields game)
        => new()
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            SenderNick = nick,
            DisplayText = game.RoomName,
            Game = game,
        };

    public static WafIngressEvent PromoPrivateMessage(string nick, string text)
        => new()
        {
            Kind = WafIngressKind.PrivateChat,
            Surface = WafSurface.PrivateMessage,
            SenderNick = nick,
            DisplayText = text,
            RawBody = text,
        };

    public static WafIngressEvent LobbyPromoChat(string nick, string text)
        => new()
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            Channel = "#cncnet-mg",
            SenderNick = nick,
            DisplayText = text,
            RawBody = text,
        };

    public static WafIngressEvent InviteCtcp(string nick, string args)
        => new()
        {
            Kind = WafIngressKind.PrivateCtcp,
            Surface = WafSurface.Protocol,
            SenderNick = nick,
            CtcpCommand = "INVITE",
            CtcpPayload = args,
            DisplayText = args,
            RawBody = "INVITE " + args,
        };

    public static WafIngressEvent LegitimateR13Game(string nick = "Alice")
        => new()
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            SenderNick = nick,
            DisplayText = "Casual",
            Game = new WafGameBroadcastFields
            {
                Revision = "R13",
                FieldCount = 13,
                ChannelName = "#casual-room",
                RoomName = "Casual",
                MapName = "River Raid",
                GameMode = "Standard",
                TunnelHost = "tn.example.org",
                TunnelPort = 50000,
                Players = [nick, "Bob"],
            },
        };

    /// <summary>Obfuscated promo variants that should still hit content rules after normalize/compact.</summary>
    public static IEnumerable<string> PromoTextVariants()
    {
        yield return "加群领免费代练 http://spam.vip/join";
        yield return "加\u200b群 领\u200b免\u200b费\u200b代\u200b练";
        yield return "代 练 加 群 www.boost.xyz";
        yield return "Ｄａｉ练加群 QQ:12345678"; // mixed; NFKC may fold latin fullwidth
        yield return "私聊领优惠券 扫码充值";
    }

    /// <summary>QQ / QQ群 / 群号 / 扣扣宣群引流变体（含全角数字）。</summary>
    public static IEnumerable<string> QqPromoGroupVariants()
    {
        yield return "加群 QQ:123456789 领免费代练";
        yield return "QQ群：987654321 内部优惠低价";
        yield return "q群 76543210 工作室招人";
        yield return "群号：112233445 加我私聊领券";
        yield return "加QQ 5566778899 代练折扣";
        yield return "扣扣群 334455667 陪玩充值";
        yield return "ＱＱ群：１２３４５６７８ 扫码进群"; // fullwidth digits → NFKC
        yield return "加\u200bＱＱ：８８７７６６５５ 推广广告";
        yield return "宣传群：1234567890";
        yield return "QQ 1234567890";
    }

    /// <summary>English promo / contact variants (Discord, Telegram, boost ads).</summary>
    public static IEnumerable<string> EnglishPromoVariants()
    {
        yield return "Join my Discord discord.gg/abcd123 for free boosting";
        yield return "Add me on Telegram t.me/boostshop cheap elo boost";
        yield return "DM me for coaching and rank boost special offer";
        yield return "Buy account shop - cheap boosting hire now";
        yield return "WhatsApp wa.me/15551234567 join group promo";
        yield return "Contact me for paid coaching discount today";
    }
}
