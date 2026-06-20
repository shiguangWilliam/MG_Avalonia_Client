using System;

namespace ClientAvalonia.Online.EventArguments;

public class UserNicknameEventArgs : EventArgs
{
    public UserNicknameEventArgs(string oldNickname, string newNickname)
    {
        OldNickname = oldNickname;
        NewNickname = newNickname;
    }

    public string OldNickname { get; }

    public string NewNickname { get; }
}
