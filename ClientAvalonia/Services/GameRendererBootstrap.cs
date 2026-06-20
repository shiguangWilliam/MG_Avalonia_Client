using ClientAvalonia.Domain;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>
/// Keeps ddraw / renderer files in the game directory (DX applies on options save; we also apply at client boot).
/// </summary>
public static class GameRendererBootstrap
{
    private static DirectDrawWrapperManager? _manager;
    private static bool _filesApplied;

    public static DirectDrawWrapperManager Manager
        => _manager ??= new DirectDrawWrapperManager();

    /// <summary>Call once during <see cref="Core.Startup"/> so Syringe injection sees ddraw immediately.</summary>
    public static void EnsureAppliedAtStartup()
    {
        try
        {
            Manager.ApplySelectedRenderer();
            _filesApplied = true;
            Logger.Log($"GameRendererBootstrap: applied {Manager.SelectedRenderer.InternalName} at startup (UseQres={GameProcessLauncher.UseQres}).");
        }
        catch (Exception ex)
        {
            Logger.Log("GameRendererBootstrap: startup apply failed: " + ex);
        }
    }

    /// <summary>Sync renderer windowed keys + UseQres before each launch.</summary>
    public static void RefreshBeforeLaunch()
    {
        try
        {
            Manager.ReloadSelectedRendererFromSettings();
            Manager.ApplySelectedRenderer();
            _filesApplied = true;
        }
        catch (Exception ex)
        {
            Logger.Log("GameRendererBootstrap.RefreshBeforeLaunch failed: " + ex);
        }
    }
}
