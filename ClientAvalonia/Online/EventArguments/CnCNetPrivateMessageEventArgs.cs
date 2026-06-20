using System;

namespace ClientAvalonia.Online.EventArguments;

public class CnCNetPrivateMessageEventArgs : EventArgs
{
    public CnCNetPrivateMessageEventArgs(string sender, string message)
    {
        Sender = sender;
        Message = message;
        DateTime = DateTime.Now;
    }

    public DateTime DateTime { get; set; }

    public string Sender { get; }

    public string Message { get; }
}
