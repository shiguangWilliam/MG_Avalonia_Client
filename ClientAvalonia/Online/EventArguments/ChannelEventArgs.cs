using System;

namespace ClientAvalonia.Online.EventArguments;

public class ChannelEventArgs : EventArgs
{
    public ChannelEventArgs(string channelName)
    {
        ChannelName = channelName;
    }

    public string ChannelName { get; }
}
