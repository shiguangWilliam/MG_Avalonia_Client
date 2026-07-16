using ClientAvalonia.IniUi.Models;
using ClientCore.Extensions;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// DX <c>AudioOptionsPanel</c> creates all controls in code; OptionsWindow.ini has no Audio section.
/// Inject a minimal Avalonia tree into <c>AudioOptionsPanel</c>.
/// </summary>
internal static class OptionsAudioControlsBootstrap
{
    public static void Apply(UiNodeTree tree)
    {
        UiNode? panel = tree.FindNode("AudioOptionsPanel");
        if (panel == null)
            return;

        EnsureLabel(panel, "lblScoreVolume", "Music Volume:".L10N("Client:DTAConfig:MusicVolume"));
        EnsureValueLabel(panel, "lblScoreVolumeValue");
        EnsureTrackbar(panel, "trbScoreVolume");

        EnsureLabel(panel, "lblSoundVolume", "Sound Volume:".L10N("Client:DTAConfig:SoundVolume"));
        EnsureValueLabel(panel, "lblSoundVolumeValue");
        EnsureTrackbar(panel, "trbSoundVolume");

        EnsureLabel(panel, "lblVoiceVolume", "Voice Volume:".L10N("Client:DTAConfig:VoiceVolume"));
        EnsureValueLabel(panel, "lblVoiceVolumeValue");
        EnsureTrackbar(panel, "trbVoiceVolume");

        EnsureCheckBox(panel, "chkScoreShuffle", "Shuffle Music".L10N("Client:DTAConfig:ShuffleMusic"));

        EnsureLabel(panel, "lblClientVolume", "Client Volume:".L10N("Client:DTAConfig:ClientVolume"));
        EnsureValueLabel(panel, "lblClientVolumeValue");
        EnsureTrackbar(panel, "trbClientVolume");

        EnsureCheckBox(panel, "chkMainMenuMusic", "Main menu music".L10N("Client:DTAConfig:MainMenuMusic"));
        EnsureCheckBox(
            panel,
            "chkStopMusicOnMenu",
            "Don't play main menu music in lobbies".L10N("Client:DTAConfig:NoLobbiesMusic"));
        EnsureCheckBox(
            panel,
            "chkStopGameLobbyMessageAudio",
            "Don't play lobby message audio when game is running".L10N("Client:DTAConfig:NoGameLobbyMessageAudio"));
    }

    private static void EnsureLabel(UiNode panel, string id, string text)
    {
        UiNode node = EnsureChild(panel, id, "XNALabel", "DxLabel");
        node.Props["Text"] = text;
        node.Props["Width"] = 140.0;
        node.Props["Height"] = 22.0;
    }

    private static void EnsureValueLabel(UiNode panel, string id)
    {
        UiNode node = EnsureChild(panel, id, "XNALabel", "DxLabel");
        if (!node.Props.ContainsKey("Text"))
            node.Props["Text"] = "0";
        node.Props["Width"] = 28.0;
        node.Props["Height"] = 22.0;
    }

    private static void EnsureTrackbar(UiNode panel, string id)
    {
        UiNode node = EnsureChild(panel, id, "XNATrackbar", "DxSlider");
        node.Props["MinValue"] = 0.0;
        node.Props["MaxValue"] = 10.0;
        node.Props["Width"] = 280.0;
        node.Props["Height"] = 28.0;
        if (!node.Props.ContainsKey("DefaultIndex") && !node.Props.ContainsKey("SelectedIndex"))
            node.Props["DefaultIndex"] = 7;
    }

    private static void EnsureCheckBox(UiNode panel, string id, string text)
    {
        UiNode node = EnsureChild(panel, id, "XNAClientCheckBox", "DxCheckBox");
        node.Props["Text"] = text;
        node.Props["Width"] = 420.0;
        node.Props["Height"] = 24.0;
    }

    private static UiNode EnsureChild(UiNode panel, string id, string controlType, string templateKey)
    {
        UiNode? existing = panel.Children.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var node = new UiNode
        {
            Id = id,
            ControlType = controlType,
            TemplateKey = templateKey,
            WindowName = "OptionsWindow",
            Parent = panel,
        };
        panel.Children.Add(node);
        return node;
    }
}
