using System;

namespace ClientAvalonia.Online.EventArguments;

public class UserListEventArgs : EventArgs
{
    public UserListEventArgs(string channelName, string[] userNames)
    {
        ChannelName = channelName;
        UserNames = userNames;
    }

    public string ChannelName { get; }

    public string[] UserNames { get; }
}
