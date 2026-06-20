using System;

namespace ClientAvalonia.Online.EventArguments;

public class AttemptedServerEventArgs : EventArgs
{
    public AttemptedServerEventArgs(string serverName)
    {
        ServerName = serverName;
    }

    public string ServerName { get; }
}
