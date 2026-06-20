using System;

namespace ClientAvalonia.Online.EventArguments;

public class ServerMessageEventArgs : EventArgs
{
    public ServerMessageEventArgs(string message)
    {
        Message = message;
    }

    public string Message { get; }
}
