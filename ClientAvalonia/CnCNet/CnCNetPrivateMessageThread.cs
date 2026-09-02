using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// Per-peer private message thread (DX <c>PrivateMessageUser</c> analogue).
///
/// 线程安全（并发治理方案 §4 阶段 3）：Append 由 IRC 读线程调用、Messages/UnreadCount
/// 由 UI 线程读——内部以 <c>_sync</c> 保护；对外读取一律返回快照（拷贝），不泄漏内部 List。
/// </summary>
public sealed class CnCNetPrivateMessageThread
{
    private const int MaxMessages = 400;

    private readonly object _sync = new();
    private readonly List<CnCNetChatLine> _messages = [];

    public CnCNetPrivateMessageThread(string peerNick)
    {
        PeerNick = peerNick;
    }

    public string PeerNick { get; }

    /// <summary>消息快照（拷贝）——UI 枚举期间不受 IRC 线程写入影响。</summary>
    public IReadOnlyList<CnCNetChatLine> Messages
    {
        get
        {
            lock (_sync)
                return _messages.ToList();
        }
    }

    public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;

    public int UnreadCount { get; private set; }

    public void Append(CnCNetChatLine line, bool incrementUnread)
    {
        lock (_sync)
        {
            _messages.Add(line);
            if (_messages.Count > MaxMessages)
                _messages.RemoveRange(0, _messages.Count - MaxMessages);

            LastActivityUtc = DateTime.UtcNow;
            if (incrementUnread)
                UnreadCount++;
        }
    }

    /// <returns><see langword="true"/> when unread was cleared.</returns>
    public bool MarkRead()
    {
        lock (_sync)
        {
            if (UnreadCount == 0)
                return false;

            UnreadCount = 0;
            return true;
        }
    }
}
