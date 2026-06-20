using System;

namespace ClientAvalonia.Online.EventArguments;

public class ChannelModeEventArgs : EventArgs
{
    public ChannelModeEventArgs(string userName, string channelName, string modeString)
    {
        UserName = userName;
        ChannelName = channelName;
        ModeString = modeString;
    }

    public string UserName { get; }

    public string ChannelName { get; }

    public string ModeString { get; }
}
