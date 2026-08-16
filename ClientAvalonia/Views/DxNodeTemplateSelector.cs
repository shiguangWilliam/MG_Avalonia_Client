using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ClientAvalonia.Rendering;
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

    /// <summary>Maps campaign control ids to dedicated chrome templates (INI unchanged).</summary>
    private static string ResolveTemplateKey(UiNodeViewModel vm)
    {
        if (DxThemeManager.IsTactical)
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

        // Classic: identical routing to main — the texture-driven campaign chrome
        // (texture backdrop + gradient shell), NOT the generic placeholder templates.
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
}
