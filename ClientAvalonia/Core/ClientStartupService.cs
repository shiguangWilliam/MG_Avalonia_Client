using ClientAvalonia.Core;

namespace ClientAvalonia.Core;

/// <summary>Backward-compatible facade over <see cref="PreStartup"/> / <see cref="Startup"/>.</summary>
public static class ClientStartupService
{
    public static bool BootstrapSucceeded => Startup.BootstrapSucceeded;

    public static string? BootstrapError => Startup.BootstrapError;

    public static bool IsUpdaterInitialized => Startup.IsUpdaterInitialized;

    public static event Action? LocalVersionsChecked
    {
        add => Startup.LocalVersionsChecked += value;
        remove => Startup.LocalVersionsChecked -= value;
    }

    /// <summary>Early-only init for UI (workspace picker). Does not bind GameRoot.</summary>
    public static void RunEarly(StartupParams parameters)
        => PreStartup.InitializeEarly(parameters);

    /// <summary>Legacy full bootstrap (CLI / forced gameRoot). Avoid in production UI.</summary>
    public static void Run(string? gameRoot = null)
        => PreStartup.Initialize(gameRoot);
}
