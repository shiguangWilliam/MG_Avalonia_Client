using ClientCore;
using System;
using System.Threading;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet;

/// <summary>Polls CnCNet live player count (ClientConfiguration.CnCNetPlayerCountURL).</summary>
public sealed class CnCNetPlayerCountService : IDisposable
{
    private const int RefreshIntervalMs = 60_000;

    private readonly CancellationTokenSource _cts = new();
    private int _playerCount = -1;

    public event Action<int>? PlayerCountUpdated;

    public int PlayerCount => _playerCount;

    public void Start()
    {
        _playerCount = FetchCount(timeoutMilliseconds: 1000);
        PlayerCountUpdated?.Invoke(_playerCount);
        ThreadPool.QueueUserWorkItem(_ => RunLoop(_cts.Token));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private void RunLoop(CancellationToken token)
    {
        while (!token.WaitHandle.WaitOne(RefreshIntervalMs))
        {
            int count = FetchCount();
            if (count == _playerCount)
                continue;

            _playerCount = count;
            PlayerCountUpdated?.Invoke(_playerCount);
        }
    }

    public static int FetchCount(int timeoutMilliseconds = 5000)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(AppState.Configuration.Legacy.CnCNetPlayerCountURL))
                return -1;

            string? info = CnCNetHttp.DownloadString(AppState.Configuration.Legacy.CnCNetPlayerCountURL, timeoutMilliseconds);
            if (string.IsNullOrWhiteSpace(info))
                return -1;

            info = info.Replace("{", string.Empty).Replace("}", string.Empty).Replace("\"", string.Empty);
            string identifier = AppState.Configuration.Legacy.CnCNetLiveStatusIdentifier;

            foreach (string value in info.Split(','))
            {
                if (value.Contains(identifier, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToInt32(value[(identifier.Length + 1)..]);
            }

            return -1;
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetPlayerCountService: fetch failed: {ex.Message}");
            return -1;
        }
    }
}
