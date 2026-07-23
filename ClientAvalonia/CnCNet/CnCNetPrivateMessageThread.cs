using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientAvalonia.CnCNet;

/// <summary>Per-peer private message thread (DX <c>PrivateMessageUser</c> analogue).</summary>
public sealed class CnCNetPrivateMessageThread
{
    private readonly List<CnCNetChatLine> _messages = [];

    public CnCNetPrivateMessageThread(string peerNick)
    {
        PeerNick = peerNick;
    }

    public string PeerNick { get; }

    public IReadOnlyList<CnCNetChatLine> Messages => _messages;

    public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;

    public int UnreadCount { get; private set; }

    public void Append(CnCNetChatLine line, bool incrementUnread)
    {
        _messages.Add(line);
        if (_messages.Count > 400)
            _messages.RemoveRange(0, _messages.Count - 400);

        LastActivityUtc = DateTime.UtcNow;
        if (incrementUnread)
            UnreadCount++;
    }

    /// <returns><see langword="true"/> when unread was cleared.</returns>
    public bool MarkRead()
    {
        if (UnreadCount == 0)
            return false;

        UnreadCount = 0;
        return true;
    }
}
