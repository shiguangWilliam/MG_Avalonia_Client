using ClientAvalonia.IniUi;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Themes;

namespace ClientAvalonia.Views;

public class DxNodeTemplateSelector : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is not UiNodeViewModel vm)
            return null;

        string key = ResolveTemplateKey(vm);
        if (Application.Current?.TryFindResource(key, out object? resource) == true
            && resource is IDataTemplate template)
            return template.Build(param);

        if (Application.Current?.TryFindResource("DxControlHost", out object? fallback) == true
            && fallback is IDataTemplate fb)
            return fb.Build(param);

        return new TextBlock { Text = vm.TemplateKey };
    }

    public bool Match(object? data) => data is UiNodeViewModel;

    /// <summary>Maps campaign / main-menu control ids to dedicated chrome templates (INI unchanged).</summary>
    private static string ResolveTemplateKey(UiNodeViewModel vm)
    {
        if (DxThemeManager.IsTactical)
        {
            if (IsMainMenu(vm))
            {
                return vm.Id.ToLowerInvariant() switch
                {
                    "mainmenu" => "DxMainMenuTacticalRoot",
                    "btnnewcampaign" => "DxMainMenuTacticalNavPrimary",
                    "btnloadgame" or "btncncnet" or "btnlan" or "btnskirmish"
                        or "btnoptions" or "btnexit" => "DxMainMenuTacticalNav",
                    "btnstatistics" or "btncredits" or "btnmapeditor" => "DxMainMenuTacticalLink",
                    "lblversion" or "lblupdatestatus" => "DxMainMenuTacticalStatusLink",
                    "lblcncnetstatus" or "lblcncnetplayercount" or "txtversion"
                        => "DxMainMenuTacticalStatus",
                    _ => vm.TemplateKey,
                };
            }

            if (IsCampaign(vm))
            {
                return vm.Id.ToLowerInvariant() switch
                {
                    "campaignselector" => "DxCampaignTacticalRoot",
                    "lbcampaignlist" => "DxCampaignTacticalListBox",
                    "tbmissiondescription" => "DxCampaignTacticalBriefing",
                    "lblselectcampaign" or "lblmissiondescriptionheader" => "DxCampaignTacticalSectionHeader",
                    "lbldifficultylevel" => "DxCampaignTacticalDifficultyHeader",
                    "gdi" or "nod" or "thirdside" or "fourthside" => "DxCampaignTacticalSideTab",
                    "lbleasy" or "lblnormal" or "lblhard" => "DxCampaignTacticalDifficultyLabel",
                    "trbdifficultyselector" => "DxCampaignTacticalDifficulty",
                    "btnlaunch" => "DxCampaignTacticalPrimaryButton",
                    "btncancel" => "DxCampaignTacticalSecondaryButton",
                    _ => vm.TemplateKey,
                };
            }

            // Options / other windows keep INI TemplateKey (footer Cancel stays DxButton).
            return vm.Id.Equals(WindowKind.CampaignSelector, StringComparison.OrdinalIgnoreCase)
                ? "DxCampaignTacticalRoot"
                : vm.TemplateKey;
        }

        // Classic: campaign chrome only inside CampaignSelector —?never hijack Options btnCancel.
        if (IsCampaign(vm))
        {
            return vm.Id.ToLowerInvariant() switch
            {
                "campaignselector" => "DxCampaignRoot",
                "lbcampaignlist" => "DxCampaignListBox",
                "tbmissiondescription" => "DxCampaignBriefing",
                "lblselectcampaign" or "lblmissiondescriptionheader" or "lbldifficultylevel" => "DxCampaignSectionHeader",
                "gdi" or "nod" or "thirdside" or "fourthside" => "DxCampaignSideTab",
                "lbleasy" or "lblnormal" or "lblhard" => "DxCampaignDifficultyLabel",
                "trbdifficultyselector" => "DxCampaignDifficultySlider",
                "btnlaunch" => "DxCampaignPrimaryButton",
                "btncancel" => "DxCampaignSecondaryButton",
                _ => vm.TemplateKey,
            };
        }

        return vm.Id.Equals(WindowKind.CampaignSelector, StringComparison.OrdinalIgnoreCase)
            ? "DxCampaignRoot"
            : vm.TemplateKey;
    }

    private static bool IsMainMenu(UiNodeViewModel vm)
        => string.Equals(vm.Node.WindowName, WindowKind.MainMenu, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Campaign chrome is keyed by control id (btnCancel/btnLaunch). Gate by window so
    /// OptionsWindow's footer btnCancel keeps <c>DxButton</c> instead of campaign glass.
    /// </summary>
    private static bool IsCampaign(UiNodeViewModel vm)
        => FloatingOverlayLayout.IsCampaignWindow(vm.Node.WindowName ?? string.Empty)
           || FloatingOverlayLayout.IsCampaignWindow(vm.Id);
}
