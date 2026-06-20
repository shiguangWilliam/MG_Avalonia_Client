using System;

namespace ClientAvalonia.Online.EventArguments;

public class ChannelCTCPEventArgs : EventArgs
{
    public ChannelCTCPEventArgs(string userName, string message)
    {
        UserName = userName;
        Message = message;
    }

    public string UserName { get; }

    public string Message { get; }
}
