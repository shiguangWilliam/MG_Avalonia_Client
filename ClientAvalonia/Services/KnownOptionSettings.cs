namespace ClientAvalonia.Services;

/// <summary>Control ids that map to user INI keys without explicit SettingSection/SettingKey in INI.</summary>
public static class KnownOptionSettings
{
    public static bool TryResolve(string controlId, out string section, out string key)
    {
        if (Mappings.TryGetValue(controlId, out (string Section, string Key) mapping))
        {
            section = mapping.Section;
            key = mapping.Key;
            return true;
        }

        section = string.Empty;
        key = string.Empty;
        return false;
    }

    private static readonly Dictionary<string, (string Section, string Key)> Mappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["chkWindowedMode"] = ("Video", "Video.Windowed"),
            ["chkPlaySoundOnGameHosted"] = ("MultiPlayer", "PlaySoundOnGameHosted"),
            ["chkNotifyOnUserListChange"] = ("MultiPlayer", "NotifyOnUserListChange"),
            ["chkSkipLoginWindow"] = ("MultiPlayer", "SkipConnectDialog"),
            ["chkDisablePrivateMessagePopup"] = ("MultiPlayer", "DisablePrivateMessagePopups"),
            ["chkPersistentMode"] = ("MultiPlayer", "PersistentMode"),
            ["chkConnectOnStartup"] = ("MultiPlayer", "AutomaticCnCNetLogin"),
            ["chkAllowGameInvitesFromFriendsOnly"] = ("MultiPlayer", "AllowGameInvitesFromFriendsOnly"),
            ["chkDiscordIntegration"] = ("MultiPlayer", "DiscordIntegration"),
            ["chkPingUnofficialTunnels"] = ("MultiPlayer", "PingCustomTunnels"),
            ["chkWriteInstallPathToRegistry"] = ("Options", "WriteInstallationPathToRegistry"),
            ["ddAllowPrivateMessagesFrom"] = ("MultiPlayer", "AllowPrivateMessagesFromState"),
            ["chkStretchMovies"] = ("Video", "StretchMovies"),
            ["chkStopMusicOnMenu"] = ("Audio", "StopMusicOnMenu"),
            ["chkBackBufferInVRAM"] = ("Video", "VideoBackBuffer"),
            ["ddDetailLevel"] = ("Options", "DetailLevel"),
            ["ddClientResolution"] = ("Video", "ClientResolution"),
            ["tbPlayerName"] = ("MultiPlayer", "Handle"),
        };
}
