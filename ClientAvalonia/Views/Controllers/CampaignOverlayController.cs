using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientCore;
using ClientCore.Extensions;

namespace ClientAvalonia.Views.Controllers;

internal sealed class CampaignOverlayController
{
    private readonly MainWindowContext _ctx;

    public CampaignOverlayController(MainWindowContext ctx)
    {
        _ctx = ctx;
    }

    public void ApplyCampaignOverlay(UiNodeViewModel root, CampaignSideFilter sideFilter = CampaignSideFilter.All)
    {
        ResourceResolver resources = _ctx.GetOverlayResources();
        GameDataBindingApplier.ApplyCampaignOverlay(root, _ctx.GameResources, _ctx.LobbySession, resources, sideFilter);
    }

    /// <summary>Active campaign tree: root panel or legacy floating overlay.</summary>
    public UiNodeViewModel? ResolveCampaignRoot()
    {
        if (FloatingOverlayLayout.IsCampaignWindow(_ctx.CurrentWindow)
            && _ctx.ActiveRoot != null)
            return _ctx.ActiveRoot;

        if (_ctx.FloatingOverlayWindow is { } overlayWindow
            && FloatingOverlayLayout.IsCampaignWindow(overlayWindow))
            return _ctx.OverlayRoot;

        return null;
    }

    public void FilterCampaignBySide(CampaignSideFilter sideFilter)
    {
        UiNodeViewModel? root = ResolveCampaignRoot();
        if (root == null)
            return;

        ApplyCampaignOverlay(root, sideFilter);
        string label = sideFilter switch
        {
            CampaignSideFilter.Allied => "同盟国联军".L10N("Client:Main:SideAllied"),
            CampaignSideFilter.Soviet => "苏维埃联盟".L10N("Client:Main:SideSoviet"),
            CampaignSideFilter.Ackville => "阿克维尔".L10N("Client:Main:SideAckville"),
            _ => "全部".L10N("Client:Main:SideAll"),
        };
        _ctx.ShowStatus($"Campaign filter: {label} ({_ctx.LobbySession.VisibleMissions.Count} missions)");
    }

    public static int ResolveCampaignDifficulty(UiNodeViewModel campaignRoot)
    {
        UiNodeViewModel? trackbar = MainWindowContext.FindVm(campaignRoot, "trbDifficultySelector");
        if (trackbar != null && trackbar.SelectedIndex >= 0)
            return Math.Clamp(trackbar.SelectedIndex, 0, 2);

        return Math.Clamp(UserINISettings.Instance.Difficulty, 0, 2);
    }
}
