using ClientCore;
using ClientCore.Network;

namespace ClientAvalonia.Services;

/// <summary>Channel / browser lobby presentation state backed by <see cref="CnCNetSessionService"/>.</summary>
public sealed class MultiplayerLobbyState
{
    private const int MaxConnectionLogLines = 80;
    private readonly List<string> _connectionLog = [];

    public string LocalPlayerName { get; private set; } = ProgramConstants.PLAYERNAME;

    public string ConnectionStatus { get; private set; } = "Offline";

    public string ChatChannelDisplay { get; private set; } = string.Empty;

    public IReadOnlyList<string> ChannelPlayers { get; private set; } = [];

    public IReadOnlyList<string> HostedGames { get; private set; } = [];

    public IReadOnlyList<CnCNetHostedGameSummary> HostedGameDetails { get; private set; } = [];

    public int SelectedGameIndex { get; set; } = -1;

    public int OnlinePlayerCount { get; private set; } = -1;

    public IReadOnlyList<string> ConnectionLog => _connectionLog;

    public CnCNetHostedGameSummary? GetSelectedGame()
    {
        if (SelectedGameIndex < 0 || SelectedGameIndex >= HostedGameDetails.Count)
            return null;

        return HostedGameDetails[SelectedGameIndex];
    }

    public void RefreshFromCore(string? localName = null)
    {
        LocalPlayerName = string.IsNullOrWhiteSpace(localName)
            ? ProgramConstants.PLAYERNAME
            : localName!;
    }

    public void SetConnectionStatus(string status) => ConnectionStatus = status;

    public void SetChannelName(string uiName, string chatChannel)
        => ChatChannelDisplay = string.IsNullOrWhiteSpace(uiName) ? chatChannel : uiName;

    public void SetChannelPlayers(IReadOnlyList<string> players) => ChannelPlayers = players;

    public void SetHostedGames(IReadOnlyList<CnCNetHostedGameSummary> games)
    {
        HostedGameDetails = games;
        HostedGames = games.Select(g => g.DisplayLine).ToList();
        if (SelectedGameIndex >= games.Count)
            SelectedGameIndex = games.Count > 0 ? 0 : -1;
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
}
