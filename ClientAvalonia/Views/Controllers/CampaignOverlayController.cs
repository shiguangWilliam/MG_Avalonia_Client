using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientCore;

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

    public void FilterCampaignBySide(CampaignSideFilter sideFilter)
    {
        if (_ctx.OverlayRoot == null)
            return;

        ApplyCampaignOverlay(_ctx.OverlayRoot, sideFilter);
        string label = sideFilter switch
        {
            CampaignSideFilter.Allied => "同盟国联军",
            CampaignSideFilter.Soviet => "苏维埃联盟",
            CampaignSideFilter.Ackville => "阿克维尔",
            _ => "全部",
        };
        _ctx.ShowStatus($"Campaign filter: {label} ({_ctx.LobbySession.VisibleMissions.Count} missions)");
    }

    public static int ResolveCampaignDifficulty(UiNodeViewModel overlayRoot)
    {
        UiNodeViewModel? trackbar = MainWindowContext.FindVm(overlayRoot, "trbDifficultySelector");
        if (trackbar != null && trackbar.SelectedIndex >= 0)
            return Math.Clamp(trackbar.SelectedIndex, 0, 2);

        return Math.Clamp(UserINISettings.Instance.Difficulty, 0, 2);
    }
}
