using ClientCore;
using ClientCore.Settings;
using Rampastring.Tools;
using System;
using System.Threading;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// Keeps CnCNet game-room presence during slow Syringe/Ares startup.
/// Real mode: accelerated ICMP + TNLPNG. Synthetic mode (INI): cached TNLPNG without ICMP + re-JOIN.
/// </summary>
internal sealed class CnCNetLaunchPresenceKeepAlive : IDisposable
{
    private const double IntervalSeconds = 10;

    private readonly CnCNetSession _session;
    private Timer? _timer;
    private bool _synthetic;
    private int _lastSuccessfulPingMs = -1;

    public CnCNetLaunchPresenceKeepAlive(CnCNetSession session)
    {
        _session = session;
    }

    public void Start(bool syntheticHeartbeats)
    {
        _synthetic = syntheticHeartbeats;
        _timer?.Dispose();
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(IntervalSeconds));
        Logger.Log(
            $"CnCNetLaunchPresenceKeepAlive: started (synthetic={syntheticHeartbeats}, interval={IntervalSeconds}s).");
    }

    public void Stop()
    {
        if (_timer == null)
            return;

        _timer.Dispose();
        _timer = null;
        Logger.Log("CnCNetLaunchPresenceKeepAlive: stopped.");
    }

    public bool IsActive => _timer != null;

    public void NoteSuccessfulPing(int pingMs)
    {
        if (pingMs >= 0)
            _lastSuccessfulPingMs = pingMs;
    }

    private void Tick()
    {
        if (!ProgramConstants.IsLaunchingGame && !ProgramConstants.IsInGame)
            return;

        try
        {
            if (_synthetic)
                _session.RunSyntheticLaunchPresenceHeartbeat(_lastSuccessfulPingMs);
            else
                _session.RunAcceleratedLaunchPresenceHeartbeat(ping => NoteSuccessfulPing(ping));
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetLaunchPresenceKeepAlive: tick failed: {ex.Message}");
        }
    }

    public void Dispose() => Stop();
}
