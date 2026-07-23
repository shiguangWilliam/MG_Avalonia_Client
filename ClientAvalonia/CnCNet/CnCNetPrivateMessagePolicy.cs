using System;
using ClientCore.Enums;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// DX-aligned private-message accept rules (<c>AllowPrivateMessagesFromState</c>).
/// Friend list is not yet ported; <see cref="AllowPrivateMessagesFromEnum.Friends"/>
/// falls back to current-channel membership.
/// <para>
/// Pipeline (keep coarse filter before WAF):
/// <c>IRC PRIVMSG → ShouldAccept (source policy) → reject: no session / no WAF
/// → accept: Waf.Evaluate → Warn/Drop/Allow → UI</c>.
/// Choosing <see cref="AllowPrivateMessagesFromEnum.All"/> skips the source filter;
/// content protection then relies entirely on the local ingress WAF.
/// </para>
/// </summary>
public static class CnCNetPrivateMessagePolicy
{
    public static bool ShouldAccept(
        AllowPrivateMessagesFromEnum policy,
        bool senderInCurrentChatChannel,
        bool isFriend = false)
    {
        return policy switch
        {
            AllowPrivateMessagesFromEnum.None => false,
            AllowPrivateMessagesFromEnum.All => true,
            AllowPrivateMessagesFromEnum.Friends => isFriend || senderInCurrentChatChannel,
            AllowPrivateMessagesFromEnum.CurrentChannel => senderInCurrentChatChannel,
            _ => true,
        };
    }

    public static AllowPrivateMessagesFromEnum FromUserSettings()
    {
        try
        {
            int raw = ClientCore.UserINISettings.Instance.AllowPrivateMessagesFromState.Value;
            return Enum.IsDefined(typeof(AllowPrivateMessagesFromEnum), raw)
                ? (AllowPrivateMessagesFromEnum)raw
                : AllowPrivateMessagesFromEnum.All;
        }
        catch
        {
            return AllowPrivateMessagesFromEnum.All;
        }
    }

    public static bool PopupsDisabled()
    {
        try
        {
            return ClientCore.UserINISettings.Instance.DisablePrivateMessagePopups.Value;
        }
        catch
        {
            return false;
        }
    }
}
