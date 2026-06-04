using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Schema;

/// <summary>Registers all base DX/XNAUI control types and shared INI property schemas.</summary>
public static class DefaultControlRegistry
{
    public static ControlRegistry Create()
    {
        var registry = new ControlRegistry();

        IReadOnlyList<IniPropertyDefinition> panelProps = Merge(CommonVisual(), Layout(), PanelSpecific());
        IReadOnlyList<IniPropertyDefinition> buttonProps = Merge(CommonVisual(), Layout(), ButtonSpecific());
        IReadOnlyList<IniPropertyDefinition> labelProps = Merge(CommonVisual(), Layout(), LabelSpecific());
        IReadOnlyList<IniPropertyDefinition> checkProps = Merge(CommonVisual(), Layout(), CheckBoxSpecific(), GameOptionCheckBox());
        IReadOnlyList<IniPropertyDefinition> dropProps = Merge(CommonVisual(), Layout(), DropDownSpecific(), GameOptionDropDown());
        IReadOnlyList<IniPropertyDefinition> textProps = Merge(CommonVisual(), Layout(), TextBoxSpecific());
        IReadOnlyList<IniPropertyDefinition> listProps = Merge(CommonVisual(), Layout(), ListBoxSpecific());

        Register(registry, "XNAPanel", "DxPanel", "XNAPanel", panelProps);
        Register(registry, "XNAExtraPanel", "DxPanel", "XNAPanel", panelProps);
        Register(registry, "XNAButton", "DxButton", "XNAButton", buttonProps);
        Register(registry, "XNAClientButton", "DxButton", "XNAButton", buttonProps);
        Register(registry, "GameLaunchButton", "DxButton", "XNAClientButton", buttonProps);
        Register(registry, "XNALinkButton", "DxLinkButton", "XNALinkButton", Merge(buttonProps, LinkButtonSpecific()));
        Register(registry, "XNALabel", "DxLabel", "XNALabel", labelProps);
        Register(registry, "XNATextBlock", "DxLabel", "XNATextBlock", labelProps);
        Register(registry, "XNALinkLabel", "DxLinkLabel", "XNALabel", labelProps);
        Register(registry, "XNAClientLinkLabel", "DxLinkLabel", "XNALabel", labelProps);
        Register(registry, "XNACheckBox", "DxCheckBox", "XNACheckBox", checkProps);
        Register(registry, "XNAClientCheckBox", "DxCheckBox", "XNACheckBox", checkProps);
        Register(registry, "GameLobbyCheckBox", "DxCheckBox", "GameSessionCheckBox", checkProps);
        Register(registry, "GameSessionCheckBox", "DxCheckBox", "GameSessionCheckBox", checkProps);
        Register(registry, "CampaignCheckBox", "DxCheckBox", "GameSessionCheckBox", checkProps);
        Register(registry, "SettingCheckBox", "DxCheckBox", "SettingCheckBox", Merge(checkProps, SettingCheckBoxSpecific()));
        Register(registry, "FileSettingCheckBox", "DxCheckBox", "SettingCheckBox", Merge(checkProps, SettingCheckBoxSpecific()));
        Register(registry, "XNADropDown", "DxComboBox", "XNADropDown", dropProps);
        Register(registry, "XNAClientDropDown", "DxComboBox", "XNADropDown", dropProps);
        Register(registry, "GameLobbyDropDown", "DxComboBox", "GameSessionDropDown", dropProps);
        Register(registry, "GameSessionDropDown", "DxComboBox", "GameSessionDropDown", dropProps);
        Register(registry, "CampaignDropDown", "DxComboBox", "GameSessionDropDown", dropProps);
        Register(registry, "SettingDropDown", "DxComboBox", "SettingDropDown", Merge(dropProps, SettingDropDownSpecific()));
        Register(registry, "FileSettingDropDown", "DxComboBox", "SettingDropDown", Merge(dropProps, SettingDropDownSpecific()));
        Register(registry, "XNATextBox", "DxTextBox", "XNATextBox", textProps);
        Register(registry, "XNASuggestionTextBox", "DxTextBox", "XNASuggestionTextBox", Merge(textProps, SuggestionSpecific()));
        Register(registry, "XNAChatTextBox", "DxTextBox", "XNASuggestionTextBox", Merge(textProps, SuggestionSpecific()));
        Register(registry, "XNAPasswordBox", "DxTextBox", "XNATextBox", textProps);
        Register(registry, "XNAListBox", "DxListBox", "XNAListBox", listProps);
        Register(registry, "XNAMultiColumnListBox", "DxListBox", "XNAMultiColumnListBox", listProps);
        Register(registry, "ChatListBox", "DxListBox", "XNAListBox", listProps);
        Register(registry, "XNAProgressBar", "DxProgressBar", "XNAProgressBar", Merge(CommonVisual(), Layout()));
        Register(registry, "XNATrackbar", "DxSlider", "XNATrackbar", Merge(CommonVisual(), Layout()));
        Register(registry, "XNAScrollPanel", "DxScrollViewer", "XNAScrollPanel", Merge(panelProps));
        Register(registry, "XNATabControl", "DxTabControl", "XNATabControl", Merge(CommonVisual(), Layout()));
        Register(registry, "XNAClientTabControl", "DxTabControl", "XNATabControl", Merge(CommonVisual(), Layout()));
        Register(registry, "XNAIndicator", "DxIndicator", "XNAIndicator", labelProps);
        Register(registry, "MapPreviewBox", "MapPreviewBox", "MapPreviewBox", panelProps);
        Register(registry, "XNAControl", "DxControlHost", "XNAControl", Merge(CommonVisual(), Layout()));

        return registry;
    }

    private static void Register(ControlRegistry registry, string iniName, string template, string fallback, IReadOnlyList<IniPropertyDefinition> props)
        => registry.Register(new ControlTypeDefinition(iniName, template, fallback, props));

    private static IReadOnlyList<IniPropertyDefinition> CommonVisual() =>
    [
        new("Text", IniPropertyKind.String, Localizable: true, AvaloniaPropName: "Text"),
        new("Enabled", IniPropertyKind.Bool, AvaloniaPropName: "IsEnabled"),
        new("Visible", IniPropertyKind.Bool, AvaloniaPropName: "IsVisible"),
        new("ToolTip", IniPropertyKind.String, Localizable: true),
        new("DrawOrder", IniPropertyKind.Int, AvaloniaPropName: "ZIndex"),
        new("FontIndex", IniPropertyKind.Int),
        new("FontSize", IniPropertyKind.Int),
        new("IconTexture", IniPropertyKind.TexturePath),
        new("SideIconTexture", IniPropertyKind.TexturePath),
        new("SideName", IniPropertyKind.String),
        new("NoPreviewTexture", IniPropertyKind.TexturePath),
        new("PreviewFallbackTexture", IniPropertyKind.TexturePath),
        new("$LeftClickAction", IniPropertyKind.String),
    ];

    private static IReadOnlyList<IniPropertyDefinition> Layout() =>
    [
        new("X", IniPropertyKind.Int, AvaloniaPropName: "CanvasLeft"),
        new("Y", IniPropertyKind.Int, AvaloniaPropName: "CanvasTop"),
        new("Width", IniPropertyKind.Expression, AvaloniaPropName: "Width"),
        new("Height", IniPropertyKind.Expression, AvaloniaPropName: "Height"),
        new("$X", IniPropertyKind.Expression, AvaloniaPropName: "CanvasLeft"),
        new("$Y", IniPropertyKind.Expression, AvaloniaPropName: "CanvasTop"),
        new("$Width", IniPropertyKind.Expression, AvaloniaPropName: "Width"),
        new("$Height", IniPropertyKind.Expression, AvaloniaPropName: "Height"),
        new("Location", IniPropertyKind.Location),
        new("Size", IniPropertyKind.Size),
        new("DistanceFromRightBorder", IniPropertyKind.Expression),
        new("DistanceFromBottomBorder", IniPropertyKind.Expression),
        new("FillWidth", IniPropertyKind.Expression),
        new("FillHeight", IniPropertyKind.Expression),
    ];

    private static IReadOnlyList<IniPropertyDefinition> PanelSpecific() =>
    [
        new("BackgroundTexture", IniPropertyKind.TexturePath, AvaloniaPropName: "Background"),
        new("SolidColorBackgroundTexture", IniPropertyKind.RgbaColor),
        new("DrawMode", IniPropertyKind.Enum),
        new("DrawBorders", IniPropertyKind.Bool),
        new("RemapColor", IniPropertyKind.RgbaColor),
    ];

    private static IReadOnlyList<IniPropertyDefinition> ButtonSpecific() =>
    [
        new("IdleTexture", IniPropertyKind.TexturePath),
        new("HoverTexture", IniPropertyKind.TexturePath),
        new("ActiveTexture", IniPropertyKind.TexturePath),
        new("HoverSoundEffect", IniPropertyKind.SoundPath),
        new("ClickSoundEffect", IniPropertyKind.SoundPath),
        new("MatchTextureSize", IniPropertyKind.Bool),
        new("RemapColor", IniPropertyKind.RgbaColor),
        new("TextColor", IniPropertyKind.RgbColor, AvaloniaPropName: "Foreground"),
    ];

    private static IReadOnlyList<IniPropertyDefinition> LinkButtonSpecific() =>
    [
        new("URL", IniPropertyKind.Url, Localizable: true),
        new("UnixURL", IniPropertyKind.Url, Localizable: true),
    ];

    private static IReadOnlyList<IniPropertyDefinition> LabelSpecific() =>
    [
        new("$TextAnchor", IniPropertyKind.Enum),
        new("$AnchorPoint", IniPropertyKind.Opaque),
        new("RemapColor", IniPropertyKind.RgbaColor),
        new("IdleColor", IniPropertyKind.RgbColor),
        new("HoverColor", IniPropertyKind.RgbColor),
        new("TextColor", IniPropertyKind.RgbColor, AvaloniaPropName: "Foreground"),
    ];

    private static IReadOnlyList<IniPropertyDefinition> CheckBoxSpecific() =>
    [
        new("Checked", IniPropertyKind.Bool, AvaloniaPropName: "IsChecked"),
        new("CheckedMP", IniPropertyKind.Bool),
        new("AllowChecking", IniPropertyKind.Bool),
    ];

    private static IReadOnlyList<IniPropertyDefinition> GameOptionCheckBox() =>
    [
        new("SpawnIniOption", IniPropertyKind.String),
        new("CustomIniPath", IniPropertyKind.String),
        new("Reversed", IniPropertyKind.Bool),
        new("EnabledSpawnIniValue", IniPropertyKind.String),
        new("DisabledSpawnIniValue", IniPropertyKind.String),
        new("MapScoringMode", IniPropertyKind.Enum),
        new("DisallowedSideIndices", IniPropertyKind.CommaList),
        new("DisallowedSideIndex", IniPropertyKind.CommaList),
    ];

    private static IReadOnlyList<IniPropertyDefinition> DropDownSpecific() =>
    [
        new("Items", IniPropertyKind.CommaList),
        new("ItemLabels", IniPropertyKind.CommaList),
        new("DefaultIndex", IniPropertyKind.Int),
    ];

    private static IReadOnlyList<IniPropertyDefinition> GameOptionDropDown() =>
    [
        new("DataWriteMode", IniPropertyKind.Enum),
        new("SpawnIniOption", IniPropertyKind.String),
        new("OptionName", IniPropertyKind.String, Localizable: true),
    ];

    private static IReadOnlyList<IniPropertyDefinition> TextBoxSpecific() =>
    [
        new("Suggestion", IniPropertyKind.String, Localizable: true),
        new("MaxLength", IniPropertyKind.Int),
    ];

    private static IReadOnlyList<IniPropertyDefinition> SuggestionSpecific() =>
    [
        new("Suggestion", IniPropertyKind.String, Localizable: true, AvaloniaPropName: "Watermark"),
    ];

    private static IReadOnlyList<IniPropertyDefinition> ListBoxSpecific() =>
    [
        new("SolidColorBackgroundTexture", IniPropertyKind.RgbaColor),
    ];

    private static IReadOnlyList<IniPropertyDefinition> SettingCheckBoxSpecific() =>
    [
        new("DefaultValue", IniPropertyKind.Bool),
        new("SettingSection", IniPropertyKind.String),
        new("SettingKey", IniPropertyKind.String),
        new("RestartRequired", IniPropertyKind.Bool),
        new("ParentCheckBoxName", IniPropertyKind.String),
        new("ParentCheckBoxRequiredValue", IniPropertyKind.Bool),
        new("WriteSettingValue", IniPropertyKind.Bool),
        new("EnabledSettingValue", IniPropertyKind.String),
        new("DisabledSettingValue", IniPropertyKind.String),
    ];

    private static IReadOnlyList<IniPropertyDefinition> SettingDropDownSpecific() =>
    [
        new("SettingSection", IniPropertyKind.String),
        new("SettingKey", IniPropertyKind.String),
        new("RestartRequired", IniPropertyKind.Bool),
        new("WriteItemValue", IniPropertyKind.Bool),
    ];

    private static IReadOnlyList<IniPropertyDefinition> Merge(params IReadOnlyList<IniPropertyDefinition>[] groups)
    {
        var map = new Dictionary<string, IniPropertyDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (IReadOnlyList<IniPropertyDefinition> group in groups)
        {
            foreach (IniPropertyDefinition prop in group)
                map[prop.Key] = prop;
        }

        return map.Values.ToList();
    }
}
