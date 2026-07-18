using System.Runtime.InteropServices;

namespace ClientAvalonia.Services;

/// <summary>
/// Launch-time policy (Syringe chain, QRes) aligned with DX <c>GameProcessLogic</c>.
/// Renderer <see cref="Domain.DirectDrawWrapper.UseQres"/> comes from Renderers.ini per mod.
/// </summary>
public static class GameLaunchPolicy
{
    /// <summary>
    /// DX uses QRes only for windowed mode when the selected renderer enables it.
    /// Fullscreen launch goes direct (no QRes), matching DXMainClient behavior.
    /// </summary>
    public static bool ShouldUseQres(bool isWindows, bool qresFileExists, bool rendererUseQres, bool windowed)
        => isWindows && qresFileExists && rendererUseQres && windowed;

    public static bool IsWindowsPlatform()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Resolve whether the launch chain uses Syringe (or another launcher wrapper).
    /// </summary>
    public static bool UsesSyringeLauncher(string? gameLauncherExecutableName)
        => !string.IsNullOrWhiteSpace(gameLauncherExecutableName)
           && gameLauncherExecutableName.Contains("syringe", StringComparison.OrdinalIgnoreCase);
}
