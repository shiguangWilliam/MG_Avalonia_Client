namespace ClientAvalonia.CnCNet;

/// <summary>
/// Discrete GAME CTCP broadcast intervals (seconds) the host client may use.
/// Emit cadence is entirely client-local; receivers prune stale listings with their own TTL.
/// </summary>
public static class CnCNetGameBroadcastIntervals
{
    /// <summary>Arithmetic sequence 5..30 step 5.</summary>
    public static readonly int[] AllowedSeconds = [5, 10, 15, 20, 25, 30];

    public const int DefaultSeconds = 30;

    public const int MinSeconds = 5;

    public const int MaxSeconds = 30;

    public const int StepSeconds = 5;

    /// <summary>Clamp/snap an arbitrary value onto <see cref="AllowedSeconds"/>.</summary>
    public static int Snap(int seconds)
    {
        if (seconds <= AllowedSeconds[0])
            return AllowedSeconds[0];

        int best = AllowedSeconds[0];
        int bestDist = Math.Abs(seconds - best);
        for (int i = 1; i < AllowedSeconds.Length; i++)
        {
            int candidate = AllowedSeconds[i];
            int dist = Math.Abs(seconds - candidate);
            if (dist < bestDist)
            {
                best = candidate;
                bestDist = dist;
            }
        }

        return best;
    }

    public static string ComboItemsCsv
        => string.Join(",", AllowedSeconds);

    public static int DefaultComboIndex
        => Array.IndexOf(AllowedSeconds, DefaultSeconds);
}
