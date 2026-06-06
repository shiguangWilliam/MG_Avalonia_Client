using ClientCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientAvalonia.CnCNet;

/// <summary>CnCNet channel / browser lobby state (players, games, connection log).</summary>
public sealed class CnCNetLobbyState
{
    private const int MaxConnectionLogLines = 80;
    private readonly List<string> _connectionLog = [];

    public string LocalPlayerName { get; private set; } = ProgramConstants.PLAYERNAME;

    public string ConnectionStatus { get; private set; } = "Offline";

    public string ChatChannelDisplay { get; private set; } = string.Empty;

    public IReadOnlyList<string> AvailableChannelNames { get; private set; } = [];

    public int SelectedChannelIndex { get; private set; }

    public IReadOnlyList<string> ChannelPlayers { get; private set; } = [];

    public IReadOnlyList<string> HostedGames { get; private set; } = [];

    public IReadOnlyList<CnCNetHostedGameSummary> HostedGameDetails { get; private set; } = [];

    public int OnlinePlayerCount { get; private set; } = -1;

    public IReadOnlyList<string> ConnectionLog => _connectionLog;

    private readonly List<CnCNetChatLine> _chatLines = [];

    public IReadOnlyList<CnCNetChatLine> ChatLines => _chatLines;

    public int SelectedChatColorIndex { get; set; } = -1;

    public void RefreshFromCore(string? localName = null)
    {
        LocalPlayerName = string.IsNullOrWhiteSpace(localName)
            ? ProgramConstants.PLAYERNAME
            : localName!;
    }

    public void SetConnectionStatus(string status) => ConnectionStatus = status;

    public void SetChannelName(string uiName, string chatChannel)
        => ChatChannelDisplay = string.IsNullOrWhiteSpace(uiName) ? chatChannel : uiName;

    public void SetAvailableChannels(IReadOnlyList<string> names, int selectedIndex)
    {
        AvailableChannelNames = names;
        SelectedChannelIndex = selectedIndex >= 0 && selectedIndex < names.Count ? selectedIndex : 0;
    }

    public void SetChannelPlayers(IReadOnlyList<string> players) => ChannelPlayers = players;

    public void SetHostedGames(IReadOnlyList<CnCNetHostedGameSummary> games)
    {
        HostedGameDetails = games;
        HostedGames = games.Select(g => g.DisplayLine).ToList();
    }

    public void SetOnlinePlayerCount(int count) => OnlinePlayerCount = count;

    public void ClearConnectionLog() => _connectionLog.Clear();

    public void AppendConnectionLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        string line = $"{DateTime.Now:HH:mm:ss}  {message.Trim()}";
        _connectionLog.Add(line);
        if (_connectionLog.Count > MaxConnectionLogLines)
            _connectionLog.RemoveAt(0);
    }

    public void AddChatLine(CnCNetChatLine line)
    {
        _chatLines.Add(line);
        if (_chatLines.Count > MaxConnectionLogLines)
            _chatLines.RemoveAt(0);
    }

    public void ClearChatLines() => _chatLines.Clear();
}
