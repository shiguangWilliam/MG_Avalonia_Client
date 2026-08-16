using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClientAvalonia.Assets;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Views;

/// <summary>
/// Tactical campaign three-column console. Reparents INI-defined child nodes by ID
/// into dedicated columns so the globe gets a real middle column instead of being
/// stacked behind the list/briefing columns. Difficulty lives at the bottom of
/// the briefing column; Launch/Cancel stay in the floating bottom action bar.
/// Applies GLM art plates (starfield backdrop + panel texture) when available.
/// </summary>
public partial class DxCampaignTacticalLayout : UserControl
{
    public DxCampaignTacticalLayout()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => DistributeChildren();
        AttachedToVisualTree += (_, _) =>
        {
            ApplyArtPlates();
            DistributeChildren();
        };
    }

    private void ApplyArtPlates()
    {
        Bitmap? starfield = GlmAssets.Starfield;
        if (starfield != null && StarfieldPlate != null)
            StarfieldPlate.Source = starfield;

        Bitmap? panel = GlmAssets.TacticalPanel;
        if (panel == null)
            return;

        var brush = new ImageBrush(panel)
        {
            Stretch = Stretch.UniformToFill,
            Opacity = 0.55,
        };

        if (LeftPanel != null)
            LeftPanel.Background = brush;
        if (RightPanel != null)
            RightPanel.Background = brush;
    }

    public void DistributeChildren()
    {
        if (DataContext is not UiNodeViewModel vm)
            return;

        SideTabs.Children.Clear();
        ButtonsRow.Children.Clear();
        MiscHost.Children.Clear();

        foreach (UiNodeViewModel child in vm.Children)
        {
            Control wrapped = Wrap(child);
            switch (child.Id)
            {
                case "gdi":
                case "nod":
                case "thirdside":
                case "fourthside":
                    SideTabs.Children.Add(Wrap(child));
                    break;

                case "lbCampaignList":
                case "lbCampaign":
                    ListHost.Content = wrapped;
                    break;

                case "tbMissionDescription":
                case "tbCampaignDescription":
                    BriefingHost.Content = wrapped;
                    break;

                case "lblMissionDescriptionHeader":
                    MiscHost.Children.Add(wrapped);
                    break;

                case "trbDifficultySelector":
                case "trkDifficulty":
                    DifficultyHost.Content = wrapped;
                    break;

                case "lblDifficultyLevel":
                    DifficultyHeaderHost.Content = wrapped;
                    break;

                case "lblEasy":
                case "lblNormal":
                case "lblHard":
                case "lblDifficultyNames":
                    MiscHost.Children.Add(wrapped);
                    break;

                case "btnLaunch":
                case "btnStartGame":
                    ButtonsRow.Children.Add(wrapped);
                    break;

                case "btnCancel":
                    ButtonsRow.Children.Add(wrapped);
                    break;

                default:
                    MiscHost.Children.Add(wrapped);
                    break;
            }
        }
    }

    private static Control Wrap(UiNodeViewModel child)
        => new ContentControl { Content = child, ContentTemplate = new DxNodeTemplateSelector() };
}
