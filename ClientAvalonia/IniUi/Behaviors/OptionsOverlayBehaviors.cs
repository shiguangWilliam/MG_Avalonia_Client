namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>Save/Cancel on the options floating panel (does not replace main-window navigation).</summary>
public static class OptionsOverlayBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host)
    {
        registry.Register("btnSave", _ =>
        {
            if (!host.IsFloatingOverlayOpen)
                return;

            host.CommitSettings();
            host.ShowStatus("设置已保存");
        });

        RegisterClose(registry, host, "btnOK", commit: true);
        RegisterClose(registry, host, "btnCancel", discard: true);
    }

    private static void RegisterClose(
        BehaviorRegistry registry,
        IUiNavigationHost host,
        string controlId,
        bool commit = false,
        bool discard = false)
        => registry.Register(controlId, _ =>
        {
            if (!host.IsFloatingOverlayOpen)
                return;

            if (commit)
                host.CommitSettings();
            else if (discard)
                host.DiscardSettings();

            host.CloseFloatingOverlay();
            host.ShowStatus(commit ? "Settings saved" : "Settings closed");
        });
}
