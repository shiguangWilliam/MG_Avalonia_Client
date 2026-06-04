using ClientCore;
using ClientCore.Network;

namespace ClientAvalonia.Services;

/// <summary>View-model for channel / browser lobby UI; mirrors <see cref="CnCNetLobbyState"/> from Core.</summary>
public sealed class MultiplayerLobbyState
{
    public string LocalPlayerName { get; private set; } = ProgramConstants.PLAYERNAME;

    public string ConnectionStatus { get; private set; } = "Offline";

    public string ChatChannelDisplay { get; private set; } = string.Empty;

    public IReadOnlyList<string> ChannelPlayers { get; private set; } = [];

    public IReadOnlyList<string> HostedGames { get; private set; } = [];

    public IReadOnlyList<CnCNetHostedGameSummary> HostedGameDetails { get; private set; } = [];

    public int SelectedGameIndex { get; set; } = -1;

    public int OnlinePlayerCount { get; private set; } = -1;

    public IReadOnlyList<string> ConnectionLog { get; private set; } = [];

    public CnCNetHostedGameSummary? GetSelectedGame()
    {
        if (SelectedGameIndex < 0 || SelectedGameIndex >= HostedGameDetails.Count)
            return null;

        return HostedGameDetails[SelectedGameIndex];
    }

    public void SyncFrom(CnCNetLobbyState core)
    {
        LocalPlayerName = core.LocalPlayerName;
        ConnectionStatus = core.ConnectionStatus;
        ChatChannelDisplay = core.ChatChannelDisplay;
        ChannelPlayers = core.ChannelPlayers;
        HostedGameDetails = core.HostedGameDetails;
        HostedGames = core.HostedGames;
        OnlinePlayerCount = core.OnlinePlayerCount;
        ConnectionLog = core.ConnectionLog;

        if (SelectedGameIndex >= HostedGameDetails.Count)
            SelectedGameIndex = HostedGameDetails.Count > 0 ? 0 : -1;
    }

    public void RefreshFromCore(string? localName = null)
    {
        LocalPlayerName = string.IsNullOrWhiteSpace(localName)
            ? ProgramConstants.PLAYERNAME
            : localName!;
    }
}
