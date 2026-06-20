using System;

namespace ClientAvalonia.Online.EventArguments;

public class CTCPEventArgs : EventArgs
{
    public CTCPEventArgs(string sender, string channelName, string ctcpMessage)
    {
        Sender = sender;
        ChannelName = channelName;
        CTCPMessage = ctcpMessage;
    }

    public string Sender { get; }

    public string ChannelName { get; }

    public string CTCPMessage { get; }
}
