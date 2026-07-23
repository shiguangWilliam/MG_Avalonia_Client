using System;

namespace ClientAvalonia.Online.EventArguments;

public class CnCNetPrivateMessageEventArgs : EventArgs
{
    public CnCNetPrivateMessageEventArgs(string sender, string message)
        : this(sender, message, ident: string.Empty, host: string.Empty)
    {
    }

    public CnCNetPrivateMessageEventArgs(string sender, string message, string ident, string host)
    {
        Sender = sender;
        Message = message;
        Ident = ident ?? string.Empty;
        Host = host ?? string.Empty;
        DateTime = DateTime.Now;
    }

    public DateTime DateTime { get; set; }

    public string Sender { get; }

    public string Message { get; }

    /// <summary>IRC userident from <c>nick!ident@host</c> (may be empty in tests).</summary>
    public string Ident { get; }

    /// <summary>IRC host from <c>nick!ident@host</c> (may be empty in tests).</summary>
    public string Host { get; }
}
