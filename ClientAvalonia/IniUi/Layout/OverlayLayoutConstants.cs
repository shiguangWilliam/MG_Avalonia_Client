using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Layout;

/// <summary>
/// Issue #5: single home for MG-specific layout magic numbers that used to be
/// scattered as bare literals across OptionsWindowLayout / OptionsFooterChrome
/// / GameAssetResolver (footer geometry, footer z-order, row-pitch floor,
/// theme fallback textures).
///
/// Every value can be overridden per-window from INI via
/// <see cref="ReadUiOverride"/> — window sections declare e.g.
/// <c>FooterSaveLeft=12</c>; absent keys fall back to the DX-mirror defaults
/// below. This keeps DX parity out of the box while letting a theme retune
/// geometry without a client rebuild.
/// </summary>
public static class OverlayLayoutConstants
{
    // ---- Footer button geometry (DX OptionsWindow mirror) ----

    /// <summary>Save sits at the bottom-left corner of the dialog.</summary>
    public const int FooterSaveLeft = 12;

    /// <summary>Cancel sits at the bottom-right corner: Width - 104.</summary>
    public const int FooterCancelRightOffset = 104;

    /// <summary>Distance from dialog bottom to the footer row's top.</summary>
    public const int FooterBottomOffset = 40;

    public const double FooterButtonWidth = 92.0;
    public const double FooterButtonHeight = 32.0;

    /// <summary>
    /// Footer buttons must float above tab panels that overlap the bottom strip.
    /// </summary>
    public const int FooterZIndex = 1000;

    // ---- Player-option row auto-fit floors ----

    /// <summary>Minimum visual gap between slot rows when compressing to fit (px).</summary>
    public const int RowGapFloor = 3;

    /// <summary>Bottom breathing room kept below the last slot row (px).</summary>
    public const int PanelBottomBreathing = 4;

    // ---- Theme fallback textures (MG ships button.png, not 92pxbtn.png) ----

    public const string MgButtonIdleTexture = "button.png";
    public const string MgButtonHoverTexture = "button_c.png";
}

/// <summary>
/// INI override reader for <see cref="OverlayLayoutConstants"/> keys. Values
/// read from the window root node's Props (populated from the window INI
/// section by the tree builder); unrecognized/absent keys return the default.
/// </summary>
public static class OverlayLayoutOverrides
{
    public static int ReadInt(UiNode? windowRoot, string key, int fallback)
    {
        if (windowRoot == null)
            return fallback;

        if (windowRoot.Props.TryGetValue(key, out object? v))
        {
            if (v is int i)
                return i;
            if (int.TryParse(v?.ToString(), out int parsed))
                return parsed;
        }

        if (windowRoot.RawAttributes.TryGetValue(key, out string? raw)
            && int.TryParse(raw, out int rawParsed))
            return rawParsed;

        return fallback;
    }
}
