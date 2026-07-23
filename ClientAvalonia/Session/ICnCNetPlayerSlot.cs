namespace ClientAvalonia.Session;

/// <summary>
/// CnCNet 房间槽位：在 <see cref="IPlayerSlot"/> 基础上增加网络协议字段。
///
/// 这些字段只在 CnCNet 多人房间里有意义；Skirmish（本地遭遇战）无
/// Ready / Ping / Port 概念，所以基接口 <see cref="IPlayerSlot"/> 不包含它们。
///
/// 默认实现：<see cref="Domain.LobbyPlayerSlot"/> 同时实现两个接口，
/// 这样 Skirmish 与 CnCNet 可共用同一个具体类，避免新建并行槽位类型。
/// </summary>
public interface ICnCNetPlayerSlot : IPlayerSlot
{
    /// <summary>是否房主（CTCP PO 中 host 标记）。</summary>
    bool IsHost { get; set; }

    /// <summary>本机人类玩家是否已准备（CTCP READY 消息）。</summary>
    bool Ready { get; set; }

    /// <summary>是否启用自动准备（joiner 端 UI 选项，对应 chkAutoReady）。</summary>
    bool AutoReady { get; set; }

    /// <summary>网络延迟（毫秒。-1 = 未知 / 未测）。</summary>
    int Ping { get; set; }

    /// <summary>NAT 端口（tunnel 服务器分配的本机端口）。</summary>
    ushort Port { get; set; }
}
