namespace ClientAvalonia.IniUi.Layout;

/// <summary>Layout expression constants aligned with UIDesignConstants / GlobalThemeSettings (fallback subset).</summary>
public static class DefaultParserConstants
{
    public static IReadOnlyDictionary<string, int> Create()
        => new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["EMPTY_SPACE_SIDES"] = 6,
            ["EMPTY_SPACE_TOP"] = 6,
            ["EMPTY_SPACE_BOTTOM"] = 6,
            ["LOBBY_PANEL_SPACING"] = 6,
            ["LOBBY_EMPTY_SPACE_SIDES"] = 12,
            ["CHECKBOX_SPACING"] = 22,
            ["BUTTON_HEIGHT"] = 23,
            ["DEFAULT_BUTTON_HEIGHT"] = 23,
            ["DEFAULT_CONTROL_HEIGHT"] = 21,
            ["DEFAULT_LBL_HEIGHT"] = 12,
            ["BUTTON_WIDTH_133"] = 133,
            ["BUTTON_SPACING"] = 12,
            ["LABEL_SPACING"] = 6,
            ["GAME_OPTION_COLUMN_SPACING"] = 160,
            ["GAME_OPTION_ROW_SPACING"] = 6,
            ["GAME_OPTION_DD_WIDTH"] = 132,
            ["GAME_OPTION_DD_HEIGHT"] = 22,
        };
}
