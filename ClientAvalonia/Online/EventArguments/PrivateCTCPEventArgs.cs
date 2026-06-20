using System;

namespace ClientAvalonia.Online.EventArguments;

public class PrivateCTCPEventArgs : EventArgs
{
    public PrivateCTCPEventArgs(string sender, string message)
    {
        Sender = sender;
        Message = message;
    }

    public string Sender { get; }

    public string Message { get; }
}
