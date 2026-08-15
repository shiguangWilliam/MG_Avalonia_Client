using ClientCore;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet;

/// <summary>Supported CnCNet games and IRC channels (XNA GameCollection parity).</summary>
public sealed class CnCNetGameCollection
{
    public IReadOnlyList<CnCNetGameEntry> Games { get; private set; } = [];

    public void Initialize()
    {
        var games = new List<CnCNetGameEntry>();

        CnCNetGameEntry[] defaultGames =
        [
            Entry("dta", "Dawn of the Tiberium Age", "#cncnet-dta", "#cncnet-dta-games", "dtaicon.png"),
            Entry("ti", "Twisted Insurrection", "#cncnet-ti", "#cncnet-ti-games", "tiicon.png"),
            Entry("mo", "Mental Omega", "#cncnet-mo", "#cncnet-mo-games", "moicon.png"),
            Entry("rr", "YR Red-Resurrection", "#redres-lobby", "#redres-games", "rricon.png"),
            Entry("re", "Rise of the East", "#riseoftheeast", "#rote-games", "reicon.png"),
            Entry("cncr", "C&C: Reloaded", "#cncreloaded", "#cncreloaded-games", "cncricon.png"),
            Entry("td", "Tiberian Dawn", "#cncnet-td", "#cncnet-td-games", "tdicon.png", supported: false),
            Entry("ra", "Red Alert", "#cncnet-ra", "#cncnet-ra-games", "raicon.png"),
            Entry("d2k", "Dune 2000", "#cncnet-d2k", "#cncnet-d2k-games", "d2kicon.png", supported: false),
            Entry("ts", "Tiberian Sun", "#cncnet-ts", "#cncnet-ts-games", "tsicon.png"),
            Entry("yr", "Yuri's Revenge", "#cncnet-yr", "#cncnet-yr-games", "yricon.png"),
            Entry("ss", "Sole Survivor", "#cncnet-ss", "#cncnet-ss-games", "ssicon.png", supported: false),
        ];

        CnCNetGameEntry[] otherGames =
        [
            new CnCNetGameEntry
            {
                InternalName = "cncnet",
                UiName = "General CnCNet Chat",
                ChatChannel = "#cncnet",
                AlwaysEnabled = true,
                IconFileName = "cncneticon.png",
            },
        ];

        games.AddRange(defaultGames);
        games.AddRange(LoadCustomGames(defaultGames.Concat(otherGames).ToList()));
        games.AddRange(otherGames);
        TryAddImplicitLocalGame(games);

        Games = games;

        if (GetGameIndexFromInternalName(AppState.Configuration.Legacy.LocalGame) < 0)
        {
            Logger.Log(
                $"CnCNetGameCollection: LocalGame={AppState.Configuration.Legacy.LocalGame} not found. " +
                "Add [CustomGames] in GameCollectionConfig.ini, set CnCNetChatChannel / CnCNetGameBroadcastChannel " +
                "in ClientDefinitions.ini, or use a LocalGame id that can form #cncnet-{{id}} / #cncnet-{{id}}-games.");
        }
    }

    /// <summary>
    /// Channel funnel for LocalGame missing from built-in table / CustomGames:
    /// ClientDefinitions keys → LNOD-DX convention <c>#cncnet-{id}</c> / <c>#cncnet-{id}-games</c>.
    /// </summary>
    private static void TryAddImplicitLocalGame(List<CnCNetGameEntry> games)
    {
        string localGame = AppState.Configuration.Legacy.LocalGame;
        if (string.IsNullOrWhiteSpace(localGame))
            return;

        foreach (CnCNetGameEntry game in games)
        {
            if (localGame.Equals(game.InternalName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        if (!CnCNetLocalGameChannelResolver.TryResolve(
                localGame,
                AppState.Configuration.Legacy.CnCNetChatChannel,
                AppState.Configuration.Legacy.CnCNetGameBroadcastChannel,
                out string chat,
                out string broadcast,
                out CnCNetLocalGameChannelResolver.Source source))
        {
            return;
        }

        string id = CnCNetLocalGameChannelResolver.NormalizeInternalName(localGame);
        string uiName = AppState.Configuration.Legacy.LongGameName;
        if (string.IsNullOrWhiteSpace(uiName))
            uiName = id.ToUpperInvariant();

        Logger.Log(
            $"CnCNetGameCollection: implicit LocalGame={id} via {source}: chat={chat}, games={broadcast}.");

        games.Add(new CnCNetGameEntry
        {
            InternalName = id,
            UiName = uiName,
            ChatChannel = NormalizeChannel(chat),
            GameBroadcastChannel = NormalizeChannel(broadcast),
            IconFileName = id + "icon.png",
        });
    }

    public int GetGameIndexFromInternalName(string gameName)
    {
        for (int i = 0; i < Games.Count; i++)
        {
            if (gameName.Equals(Games[i].InternalName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    public CnCNetGameEntry? GetLocalGame()
    {
        int index = GetGameIndexFromInternalName(AppState.Configuration.Legacy.LocalGame);
        return index >= 0 ? Games[index] : null;
    }

    public IReadOnlyList<CnCNetGameEntry> GetSelectableGames()
    {
        var list = new List<CnCNetGameEntry>();
        foreach (CnCNetGameEntry game in Games)
        {
            if (!game.Supported)
                continue;

            if (string.IsNullOrWhiteSpace(game.ChatChannel))
                continue;

            list.Add(game);
        }

        return list;
    }

    public CnCNetGameEntry? FindByBroadcastChannel(string broadcastChannel)
    {
        if (string.IsNullOrWhiteSpace(broadcastChannel))
            return null;

        string normalized = NormalizeChannel(broadcastChannel);
        foreach (CnCNetGameEntry game in Games)
        {
            if (!game.HasGameBroadcast)
                continue;

            if (NormalizeChannel(game.GameBroadcastChannel!).Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return game;
        }

        return null;
    }

    private static CnCNetGameEntry Entry(
        string id,
        string uiName,
        string chat,
        string broadcast,
        string icon,
        bool supported = true)
        => new()
        {
            InternalName = id,
            UiName = uiName,
            ChatChannel = NormalizeChannel(chat),
            GameBroadcastChannel = NormalizeChannel(broadcast),
            IconFileName = icon,
            Supported = supported,
        };

    private static List<CnCNetGameEntry> LoadCustomGames(IReadOnlyList<CnCNetGameEntry> existingGames)
    {
        var customGames = new List<CnCNetGameEntry>();
        string? path = ResolveConfigPath();
        if (path == null)
            return customGames;

        var ini = new IniFile(path);
        List<string>? keys = ini.GetSectionKeys("CustomGames");
        if (keys == null)
            return customGames;

        var knownIds = new HashSet<string>(existingGames.Select(g => g.InternalName), StringComparer.OrdinalIgnoreCase);

        foreach (string key in keys)
        {
            string section = ini.GetStringValue("CustomGames", key, string.Empty);
            if (string.IsNullOrWhiteSpace(section) || !ini.SectionExists(section))
                continue;

            string id = ini.GetStringValue(section, "InternalName", string.Empty).ToLowerInvariant();
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException($"InternalName for game {section} is not defined or set to an empty value.");

            if (id.Length > ProgramConstants.GAME_ID_MAX_LENGTH)
                throw new InvalidOperationException($"InternalName for game {section} exceeds {ProgramConstants.GAME_ID_MAX_LENGTH} characters.");

            if (knownIds.Contains(id))
                throw new InvalidOperationException($"Game with InternalName {id.ToUpperInvariant()} already exists in the game collection.");

            string chat = GetIrcChannelNameFromIniFile(ini, section, "ChatChannel");
            string broadcast = GetIrcChannelNameFromIniFile(ini, section, "GameBroadcastChannel");

            string icon = ini.GetStringValue(section, "IconFilename", id + "icon.png");
            customGames.Add(new CnCNetGameEntry
            {
                InternalName = id,
                UiName = ini.GetStringValue(section, "UIName", id.ToUpperInvariant()),
                ChatChannel = chat,
                GameBroadcastChannel = broadcast,
                IconFileName = icon,
            });
            knownIds.Add(id);
        }

        return customGames;
    }

    private static string GetIrcChannelNameFromIniFile(IniFile ini, string section, string key)
    {
        string channel = ini.GetStringValue(section, key, string.Empty);

        if (string.IsNullOrEmpty(channel))
            throw new InvalidOperationException($"{key} for game {section} is not defined or set to an empty value.");

        if (channel.Contains(' ') || channel.Contains(',') || channel.Contains((char)7))
            throw new InvalidOperationException($"{key} for game {section} contains characters not allowed on IRC channel names.");

        return NormalizeChannel(channel);
    }

    private static string? ResolveConfigPath()
    {
        string basePath = SafePath.CombineFilePath(AppState.Environment.BaseResourcesPath, "GameCollectionConfig.ini");
        if (File.Exists(basePath))
            return basePath;

        string themePath = SafePath.CombineFilePath(AppState.Environment.ResourcesPath, "GameCollectionConfig.ini");
        return File.Exists(themePath) ? themePath : null;
    }

    private static string NormalizeChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return string.Empty;

        return channel.StartsWith('#') ? channel : "#" + channel;
    }
}
