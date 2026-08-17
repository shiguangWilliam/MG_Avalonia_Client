using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClientAvalonia.Assets;
using ClientAvalonia.Controls;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Views;

/// <summary>
/// Tactical campaign three-column console. Reparents INI-defined child nodes by ID
/// into dedicated columns so the globe gets a real middle column instead of being
/// stacked behind the list/briefing columns. Difficulty lives at the bottom of
/// the briefing column; Launch/Cancel stay in the floating bottom action bar.
/// Applies GLM art plates (starfield backdrop + panel texture) when available.
/// With the shared solar-system backdrop active, enter choreography zooms Earth
/// first, then fades panels and mission anchors in.
/// </summary>
public partial class DxCampaignTacticalLayout : UserControl
{
    private bool _orchestrating;

    public DxCampaignTacticalLayout()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => DistributeChildren();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyArtPlates();
        DistributeChildren();
        BeginEnterChoreographyIfNeeded();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_orchestrating)
            return;

        SolarSystemDirector.FrameAdvanced -= OnDirectorFrame;
        _orchestrating = false;
    }

    private void BeginEnterChoreographyIfNeeded()
    {
        if (!SolarSystemDirector.IsActive || _orchestrating)
            return;

        _orchestrating = true;
        ApplyRevealOpacities(SolarSystemDirector.PanelRevealOpacity, SolarSystemDirector.AnchorRevealOpacity);
        SolarSystemDirector.FrameAdvanced += OnDirectorFrame;
    }

    private void OnDirectorFrame()
    {
        if (!_orchestrating)
            return;

        ApplyRevealOpacities(SolarSystemDirector.PanelRevealOpacity, SolarSystemDirector.AnchorRevealOpacity);

        if (SolarSystemDirector.PanelRevealOpacity >= 1.0
            && SolarSystemDirector.AnchorRevealOpacity >= 1.0)
        {
            SolarSystemDirector.FrameAdvanced -= OnDirectorFrame;
            _orchestrating = false;
        }
    }

    private void ApplyRevealOpacities(double panelOpacity, double anchorOpacity)
    {
        bool panelsHit = panelOpacity > 0.05;
        if (LeftPanel != null)
        {
            LeftPanel.Opacity = panelOpacity;
            LeftPanel.IsHitTestVisible = panelsHit;
        }

        if (RightPanel != null)
        {
            RightPanel.Opacity = panelOpacity;
            RightPanel.IsHitTestVisible = panelsHit;
        }

        if (ActionBar != null)
        {
            ActionBar.Opacity = panelOpacity;
            ActionBar.IsHitTestVisible = panelsHit;
        }

        if (HudGrid != null)
        {
            foreach (Control cell in HudGrid.Children)
            {
                if (cell.Classes.Contains("accent-rail"))
                    cell.Opacity = panelOpacity;
            }
        }

        GlobeHost?.SetAnchorRevealOpacity(anchorOpacity);
    }

    private void ApplyArtPlates()
    {
        // The shared 3D solar system supplies the starfield; the PNG plate is
        // only a fallback when the backdrop is disabled.
        bool sharedSceneActive = SolarSystemDirector.IsActive;
        Bitmap? starfield = GlmAssets.Starfield;
        if (starfield != null && StarfieldPlate != null)
        {
            StarfieldPlate.Source = starfield;
            StarfieldPlate.IsVisible = !sharedSceneActive;
        }

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
