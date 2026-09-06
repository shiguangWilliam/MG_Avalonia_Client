using System.Threading;

namespace ClientAvalonia.Services;

/// <summary>
/// Issue #28: cross-thread process-launch knobs extracted from
/// GameProcessLauncher's bare static fields. Reads happen on the launch worker
/// thread while writes come from the UI thread (renderer selection), so the
/// backing ints are volatile-managed.
/// </summary>
public static class GameLaunchProcessState
{
    private static int _useQres;
    private static int _singleCoreAffinity;

    public static bool UseQres
    {
        get => Volatile.Read(ref _useQres) == 1;
        set => Volatile.Write(ref _useQres, value ? 1 : 0);
    }

    public static bool SingleCoreAffinity
    {
        get => Volatile.Read(ref _singleCoreAffinity) == 1;
        set => Volatile.Write(ref _singleCoreAffinity, value ? 1 : 0);
    }
}
