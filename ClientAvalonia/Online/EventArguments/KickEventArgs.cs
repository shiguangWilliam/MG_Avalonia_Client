using System;

namespace ClientAvalonia.Online.EventArguments;

public class KickEventArgs : EventArgs
{
    public KickEventArgs(string channelName, string userName)
    {
        ChannelName = channelName;
        UserName = userName;
    }

    public string ChannelName { get; }

    public string UserName { get; }
}
