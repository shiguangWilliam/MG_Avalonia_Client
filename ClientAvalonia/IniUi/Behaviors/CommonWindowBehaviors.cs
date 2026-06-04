using ClientAvalonia.Rendering;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>Shared INI control ids that return to the previous / main menu screen.</summary>
public static class CommonWindowBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host)
    {
        RegisterBack(registry, host, "btnLeaveGame", commit: false);
        RegisterBack(registry, host, "btnOK", commit: true);
        RegisterBack(registry, host, "btnSave", commit: true);
        RegisterBack(registry, host, "btnCancel", commit: false, discard: true);
        RegisterBack(registry, host, "btnReturnToMenu", commit: false);
        RegisterBack(registry, host, "btnClose", commit: false);
        RegisterBack(registry, host, "PlayerExtraOptionsPanel_btnClose", commit: false);
    }

    private static void RegisterBack(
        BehaviorRegistry registry,
        IUiNavigationHost host,
        string controlId,
        bool commit = false,
        bool discard = false)
        => registry.Register(controlId, vm =>
        {
            if (host.IsFloatingOverlayOpen)
            {
                if (commit)
                    host.CommitSettings();
                else if (discard)
                    host.DiscardSettings();

                host.CloseFloatingOverlay();
                host.ShowStatus(commit ? "Settings saved" : "Overlay closed");
                return;
            }

            if (host.CurrentWindow.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase))
            {
                if (commit)
                    host.CommitSettings();
                else if (discard)
                    host.DiscardSettings();
            }

            host.ShowStatus($"Back: {controlId} → MainMenu");
            host.NavigateBack();
        });
}
