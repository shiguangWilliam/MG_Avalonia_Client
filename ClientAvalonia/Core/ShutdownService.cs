using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ClientAvalonia.Services;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>
/// Centralized graceful shutdown.
/// Tears down long-lived singletons (CnCNet IRC, timers, keep-alive) and then ends the
/// Avalonia desktop lifetime. Idempotent — multiple calls only run the teardown once.
/// </summary>
/// <remarks>
/// Hooked from <see cref="App"/>, <see cref="Views.MainWindow"/>, and
/// <c>AppDomain.CurrentDomain.ProcessExit</c> so every exit path
/// (X button, btnExit, kill, logoff) lands here. <b>Does not</b> kill the game process —
/// the spawned game keeps running per DX behavior.
/// </remarks>
public static class ShutdownService
{
    private static readonly object _gate = new();
    private static bool _invoked;

    private static Action _disposeCnCNet = () => CnCNetSessionService.Instance.Dispose();

    private static Action _shutdownLifetime = () =>
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    };
    /// <summary>
    /// Runs the teardown sequence. Safe to call from any thread; re-entrancy is a no-op.
    /// Order matters: <see cref="_disposeCnCNet"/> runs first so QUIT is sent and timers
    /// are cancelled before the UI thread is torn down.
    /// </summary>
    /// <param name="reason">Logged for diagnostics (e.g. "MainWindow.Closing").</param>
    public static void Shutdown(string? reason = null)
    {
        lock (_gate)
        {
            if (_invoked)
                return;
            _invoked = true;
        }

        var sw = Stopwatch.StartNew();
        Logger.Log($"ShutdownService: begin ({reason ?? "unspecified"}).");

        // Dispose CnCNet first so the IRC QUIT is flushed while the UI thread is still alive.
        // Swallow failures: even if teardown throws, we must still shut down the lifetime
        // so the process can exit instead of hanging on a half-disposed singleton.
        try
        {
            _disposeCnCNet();
            Logger.Log($"ShutdownService: CnCNet disposed ({sw.ElapsedMilliseconds} ms).");
        }
        catch (Exception ex)
        {
            Logger.Log($"ShutdownService: CnCNet dispose threw ({sw.ElapsedMilliseconds} ms): {ex.Message}");
        }

        try
        {
            _shutdownLifetime();
        }
        catch (Exception ex)
        {
            Logger.Log($"ShutdownService: lifetime shutdown threw: {ex.Message}");
        }

        sw.Stop();
        Logger.Log($"ShutdownService: complete in {sw.ElapsedMilliseconds} ms.");
    }

    /// <summary>Test seam: replace the dispose / shutdown actions and reset idempotency.</summary>
    internal static void ConfigureForTests(Action? disposeCnCNet, Action? shutdownLifetime)
    {
        lock (_gate)
        {
            _invoked = false;
            _disposeCnCNet = disposeCnCNet ?? (() => { });
            _shutdownLifetime = shutdownLifetime ?? (() => { });
        }
    }

    /// <summary>Test seam: peek at whether <see cref="Shutdown"/> has run.</summary>
    internal static bool HasInvoked
    {
        get { lock (_gate) return _invoked; }
    }
}
