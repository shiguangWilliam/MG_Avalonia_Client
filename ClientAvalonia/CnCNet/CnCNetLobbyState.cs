using ClientCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// CnCNet channel / browser lobby state (players, games, connection log).
///
/// 线程安全（并发治理方案 §4 阶段 4）：IRC 读线程 Append/Add ↔ UI 线程读取。
/// 聊天与连接日志为有界 <see cref="ConcurrentQueue{T}"/>（入队后裁剪，消除
/// List.RemoveAt(0) 与枚举竞态）；对外一律 <c>ToArray()</c> 快照。
/// 其余快照属性（HostedGames/ChannelPlayers 等）为不可变列表整体替换，
/// 读写均为引用赋值，天然线程安全；标量用 volatile 保可见性。
/// </summary>
public sealed class CnCNetLobbyState
{
    private const int MaxConnectionLogLines = 80;
    private const int MaxChatLines = 200;

    private readonly ConcurrentQueue<string> _connectionLog = new();
    private readonly ConcurrentQueue<CnCNetChatLine> _chatLines = new();

    private volatile string _connectionStatus = "Offline";

    public string LocalPlayerName { get; private set; } = AppState.Environment.PlayerName;

    public string ConnectionStatus => _connectionStatus;

    public string ChatChannelDisplay { get; private set; } = string.Empty;

    public IReadOnlyList<string> AvailableChannelNames { get; private set; } = [];

    public int SelectedChannelIndex { get; private set; }

    public IReadOnlyList<string> ChannelPlayers { get; private set; } = [];

    public IReadOnlyList<string> HostedGames { get; private set; } = [];

    public IReadOnlyList<CnCNetHostedGameSummary> HostedGameDetails { get; private set; } = [];

    public int OnlinePlayerCount { get; private set; } = -1;

    public IReadOnlyList<string> ConnectionLog => _connectionLog.ToArray();

    public IReadOnlyList<CnCNetChatLine> ChatLines => _chatLines.ToArray();

    public int SelectedChatColorIndex { get; set; } = -1;

    public void RefreshFromCore(string? localName = null)
    {
        LocalPlayerName = string.IsNullOrWhiteSpace(localName)
            ? AppState.Environment.PlayerName
            : localName!;
    }

    public void SetConnectionStatus(string status) => _connectionStatus = status ?? string.Empty;

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
        _connectionLog.Enqueue(line);
        Trim(_connectionLog, MaxConnectionLogLines);
    }

    public void AddChatLine(CnCNetChatLine line)
    {
        _chatLines.Enqueue(line);
        Trim(_chatLines, MaxChatLines);
    }

    public void ClearChatLines() => _chatLines.Clear();

    /// <summary>
    /// 有界裁剪：多生产者场景下各自 TryDequeue 可能多裁几条——日志类数据可接受，
    /// 换取无锁（方案 §4 阶段 4 采用 ConcurrentQueue 而非加锁 List）。
    /// </summary>
    private static void Trim<T>(ConcurrentQueue<T> queue, int maxCount)
    {
        while (queue.Count > maxCount)
            queue.TryDequeue(out _);
    }
}
