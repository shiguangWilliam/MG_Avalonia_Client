using System;

namespace ClientAvalonia.Online.EventArguments;

public class ChannelTopicEventArgs : EventArgs
{
    public ChannelTopicEventArgs(string channelName, string topic)
    {
        ChannelName = channelName;
        Topic = topic;
    }

    public string ChannelName { get; }

    public string Topic { get; }
}
