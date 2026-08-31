using ClientCore;
using System;
using System.Collections.Generic;
using System.IO;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet;

/// <summary>Chat / game-broadcast IRC channels for the local mod (GameCollectionConfig.ini).</summary>
public sealed class CnCNetGameChannels
{
    public required string InternalName { get; init; }

    public required string UiName { get; init; }

    public required string ChatChannel { get; init; }

    public required string GameBroadcastChannel { get; init; }

    public static CnCNetGameChannels? LoadForLocalGame()
    {
        string localGame = AppState.Configuration.Legacy.LocalGame;
        if (string.IsNullOrWhiteSpace(localGame))
            return null;

        string? path = ResolveConfigPath();
        if (path == null)
            return null;

        var ini = new IniFile(path);
        List<string>? keys = ini.GetSectionKeys("CustomGames");
        if (keys == null)
            return null;

        foreach (string key in keys)
        {
            string section = ini.GetStringValue("CustomGames", key, string.Empty);
            if (string.IsNullOrWhiteSpace(section) || !ini.SectionExists(section))
                continue;

            string internalName = ini.GetStringValue(section, "InternalName", string.Empty);
            if (!internalName.Equals(localGame, StringComparison.OrdinalIgnoreCase))
                continue;

            string chat = NormalizeChannel(ini.GetStringValue(section, "ChatChannel", string.Empty));
            string broadcast = NormalizeChannel(ini.GetStringValue(section, "GameBroadcastChannel", string.Empty));
            if (string.IsNullOrEmpty(chat) || string.IsNullOrEmpty(broadcast))
                return null;

            return new CnCNetGameChannels
            {
                InternalName = internalName,
                UiName = ini.GetStringValue(section, "UIName", internalName),
                ChatChannel = chat,
                GameBroadcastChannel = broadcast,
            };
        }

        Logger.Log($"CnCNetGameChannels: no entry for LocalGame={localGame}.");
        return null;
    }

    /// <summary>XNA loads from <see cref="AppState.Environment.BaseResourcesPath"/> (Resources/), not the theme subfolder.</summary>
    private static string? ResolveConfigPath()
    {
        string basePath = SafePath.CombineFilePath(AppState.Environment.BaseResourcesPath, "GameCollectionConfig.ini");
        if (File.Exists(basePath))
            return basePath;

        string themePath = SafePath.CombineFilePath(AppState.Environment.ResourcesPath, "GameCollectionConfig.ini");
        if (File.Exists(themePath))
            return themePath;

        Logger.Log($"CnCNetGameChannels: GameCollectionConfig.ini not found at {basePath} or {themePath}.");
        return null;
    }

    private static string NormalizeChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return string.Empty;

        return channel.StartsWith('#') ? channel : "#" + channel;
    }
}
