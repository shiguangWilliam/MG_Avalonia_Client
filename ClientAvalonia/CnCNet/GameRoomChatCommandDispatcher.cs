using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// Parses and dispatches room chat-box slash commands.
/// Mirrors DX <c>MultiplayerGameLobby.TbChatInput_EnterPressed</c> command branch.
/// </summary>
public sealed class GameRoomChatCommandDispatcher
{
    private readonly List<GameRoomChatCommand> _commands = [];
    private readonly Func<bool> _isHost;
    private readonly Action<string> _addNotice;
    private readonly Action? _onChangeTunnelRequested;
    private readonly Func<string>? _tunnelInfoProvider;

    public GameRoomChatCommandDispatcher(
        Func<bool> isHost,
        Action<string> addNotice,
        Action? onChangeTunnelRequested = null,
        Func<string>? tunnelInfoProvider = null)
    {
        _isHost = isHost ?? throw new ArgumentNullException(nameof(isHost));
        _addNotice = addNotice ?? throw new ArgumentNullException(nameof(addNotice));
        _onChangeTunnelRequested = onChangeTunnelRequested;
        _tunnelInfoProvider = tunnelInfoProvider;
        RegisterDefaults();
    }

    public IReadOnlyList<GameRoomChatCommand> Commands => _commands;

    public void AddCommand(GameRoomChatCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
    }

    /// <summary>
    /// Attempt to handle <paramref name="rawInput"/> as a slash command.
    /// Returns <c>true</c> when the input was consumed (known or unknown command);
    /// <c>false</c> when the text is ordinary chat and should be sent as PRIVMSG.
    /// </summary>
    public bool TryHandle(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput) || !rawInput.StartsWith('/'))
            return false;

        string text = rawInput.Trim();
        string command;
        string parameters;

        int spaceIndex = text.IndexOf(' ');
        if (spaceIndex < 0)
        {
            command = text[1..].ToUpperInvariant();
            parameters = string.Empty;
        }
        else
        {
            command = text[1..spaceIndex].ToUpperInvariant();
            parameters = text[(spaceIndex + 1)..].Trim();
        }

        foreach (GameRoomChatCommand entry in _commands)
        {
            if (!string.Equals(entry.Command, command, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!_isHost() && entry.HostOnly)
            {
                _addNotice($"/{entry.Command} is for game hosts only.");
                return true;
            }

            entry.Action(parameters);
            return true;
        }

        _addNotice(BuildHelpText());
        return true;
    }

    public string BuildHelpText()
    {
        var sb = new StringBuilder("To use a command, start your message with /<command>. Possible chat box commands:");
        foreach (GameRoomChatCommand entry in _commands)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(entry.Command).Append(": ").Append(entry.Description);
        }

        return sb.ToString();
    }

    private void RegisterDefaults()
    {
        // Shared /ROLL — same dice regex as DX MultiplayerGameLobby.RollDiceCommand.
        AddCommand(new GameRoomChatCommand(
            "ROLL",
            "Roll dice, for example /roll 3d6",
            hostOnly: false,
            RollDice));

        AddCommand(new GameRoomChatCommand(
            "HIDEMAPS",
            "Hide map list (game host only)",
            hostOnly: true,
            _ => _addNotice("Map list hide requested.")));

        AddCommand(new GameRoomChatCommand(
            "SHOWMAPS",
            "Show map list (game host only)",
            hostOnly: true,
            _ => _addNotice("Map list show requested.")));

        AddCommand(new GameRoomChatCommand(
            "TUNNELINFO",
            "View tunnel server information",
            hostOnly: false,
            _ => _addNotice(_tunnelInfoProvider?.Invoke() ?? "Tunnel info unavailable.")));

        AddCommand(new GameRoomChatCommand(
            "CHANGETUNNEL",
            "Change the used CnCNet tunnel server (game host only)",
            hostOnly: true,
            _ =>
            {
                if (_onChangeTunnelRequested != null)
                    _onChangeTunnelRequested();
                else
                    _addNotice("Tunnel change UI pending.");
            }));
    }

    /// <summary>
    /// Pure dice-roll helper exposed for unit tests. Returns the notice text, or null on bad input.
    /// </summary>
    public static string? TryFormatDiceRoll(string parameters, Func<int, int>? nextIntExclusive = null)
    {
        // DX: ^(\d{1,2})([dD])(\d{1,3})$ — e.g. 3d6
        Match match = Regex.Match(parameters.Trim(), @"^(\d{1,2})([dD])(\d{1,3})$");
        if (!match.Success)
            return "Invalid dice roll. Example: /roll 3d6";

        int count = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int sides = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (count <= 0 || sides <= 0)
            return "Invalid dice roll. Example: /roll 3d6";

        nextIntExclusive ??= Random.Shared.Next;
        var results = new int[count];
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            int roll = nextIntExclusive(sides) + 1;
            results[i] = roll;
            total += roll;
        }

        return $"Dice roll ({count}d{sides}): [{string.Join(", ", results.Select(r => r.ToString(CultureInfo.InvariantCulture)))}] = {total}";
    }

    private void RollDice(string parameters)
    {
        string? notice = TryFormatDiceRoll(parameters);
        if (notice != null)
            _addNotice(notice);
    }
}
