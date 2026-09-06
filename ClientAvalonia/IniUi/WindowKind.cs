namespace ClientAvalonia.IniUi;

/// <summary>
/// Issue #19 (阶段 1): canonical window-section names. Window-name string
/// literals were scattered across ~21 files (~175 sites) — every typo-prone
/// comparison ("SkirmishLobb" vs "SkirmishLobby") failed silently because the
/// framework treats unknown names as generic windows.
///
/// Values MUST equal the INI section names shipped in Resources/*.ini
/// (case-insensitive at every call site; keep the canonical casing here).
/// </summary>
public static class WindowKind
{
    /// <summary>Main menu shell (Resources/MainMenu.ini).</summary>
    public const string MainMenu = "MainMenu";

    /// <summary>Settings overlay (Resources/OptionsWindow.ini).</summary>
    public const string OptionsWindow = "OptionsWindow";

    /// <summary>Campaign selector (Resources/CampaignSelector.ini).</summary>
    public const string CampaignSelector = "CampaignSelector";

    /// <summary>Skirmish lobby (Resources/SkirmishLobby.ini, BasedOn chain).</summary>
    public const string SkirmishLobby = "SkirmishLobby";

    /// <summary>LAN lobby browser.</summary>
    public const string LanLobby = "LANLobby";

    /// <summary>LAN game room (in-room).</summary>
    public const string LanGameLobby = "LANGameLobby";

    /// <summary>CnCNet multiplayer game room.</summary>
    public const string CnCNetGameLobby = "CnCNetGameLobby";

    /// <summary>CnCNet channel/chat browser.</summary>
    public const string ChannelLobby = "ChannelLobby";

    /// <summary>Shared multiplayer lobby base (BasedOn ancestor, never loaded directly).</summary>
    public const string MultiplayerGameLobby = "MultiplayerGameLobby";

    /// <summary>Loading screen overlay.</summary>
    public const string LoadingScreen = "LoadingScreen";

    /// <summary>Case-insensitive window-name equality — the ONLY sanctioned comparison.</summary>
    public static bool Is(string? actual, string canonical)
        => string.Equals(actual, canonical, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>True for any in-room lobby (skirmish / LAN / CnCNet).</summary>
    public static bool IsGameLobby(string? windowName)
        => Is(windowName, SkirmishLobby)
           || Is(windowName, LanGameLobby)
           || Is(windowName, CnCNetGameLobby)
           || Is(windowName, MultiplayerGameLobby);
}
