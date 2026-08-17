using ClientAvalonia.Domain;
using ClientCore;

namespace ClientAvalonia.IniUi.Behaviors;

public static class CampaignOverlayBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host)
    {
        registry.Register("btnCancel", _ =>
        {
            // Panel mode (root CampaignSelector): navigate back to MainMenu.
            // Legacy overlay path (mod-opened via $LeftClickAction) still closes
            // the floating shell.
            if (host.IsFloatingOverlayOpen
                && host.FloatingOverlayWindow is { } overlayWindow
                && Services.FloatingOverlayLayout.IsCampaignWindow(overlayWindow))
            {
                host.CloseFloatingOverlay();
                host.ShowStatus("Campaign closed");
                return;
            }

            host.ShowStatus("Back: btnCancel → MainMenu");
            host.NavigateBack();
        });

        registry.Register("btnLaunch", _ =>
        {
            if (host.TryLaunchCampaign(out string message))
                host.ShowStatus(message);
            else
                host.ShowStatus($"Campaign launch failed: {message}");
        });

        RegisterSideFilter(registry, host, "GDI", CampaignSideFilter.Allied);
        RegisterSideFilter(registry, host, "Nod", CampaignSideFilter.Soviet);
        RegisterSideFilter(registry, host, "ThirdSide", CampaignSideFilter.Ackville);

        registry.RegisterAfter("trbDifficultySelector", vm =>
        {
            UserINISettings.Instance.Difficulty.Value = Math.Clamp(vm.SelectedIndex, 0, 2);
        });
    }

    private static void RegisterSideFilter(
        BehaviorRegistry registry,
        IUiNavigationHost host,
        string controlId,
        CampaignSideFilter filter)
    {
        registry.Register(controlId, _ => host.FilterCampaignBySide(filter));
    }
}
