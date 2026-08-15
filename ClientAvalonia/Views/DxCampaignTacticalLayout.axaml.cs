using System;
using Avalonia.Controls;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Views;

/// <summary>
/// Tactical campaign three-column console. Reparents INI-defined child nodes by ID
/// into dedicated columns so the globe gets a real middle column instead of being
/// stacked behind the list/briefing columns.
/// </summary>
public partial class DxCampaignTacticalLayout : UserControl
{
    public DxCampaignTacticalLayout()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => DistributeChildren();
        AttachedToVisualTree += (_, _) => DistributeChildren();
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
                    // Replaced by the built-in BRIEFING column title.
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
                    // Tactical uses the built-in CASUAL/STANDARD/MENTAL segment.
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
                    // Unknown INI controls stay alive (bindings keep working) but hidden.
                    MiscHost.Children.Add(wrapped);
                    break;
            }
        }
    }

    private static Control Wrap(UiNodeViewModel child)
        => new ContentControl { Content = child, ContentTemplate = new DxNodeTemplateSelector() };
}
