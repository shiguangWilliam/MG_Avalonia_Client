using System;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// One slash-command entry in the room chat box.
/// Mirrors DXMainClient <c>ChatBoxCommand</c>.
/// </summary>
public sealed class GameRoomChatCommand
{
    public GameRoomChatCommand(
        string command,
        string description,
        bool hostOnly,
        Action<string> action)
    {
        Command = command;
        Description = description;
        HostOnly = hostOnly;
        Action = action;
    }

    /// <summary>Uppercase command verb without the leading slash (e.g. <c>ROLL</c>).</summary>
    public string Command { get; }

    public string Description { get; }

    public bool HostOnly { get; }

    public Action<string> Action { get; }
}
