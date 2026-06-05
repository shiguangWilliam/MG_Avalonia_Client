using ClientCore;
using ClientCore.Extensions;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientAvalonia.Services;

/// <summary>Lobby side dropdown entries (Random, RandomSelectors, sides, Spectator) aligned with GameLobbyBase.</summary>
public sealed class LobbySideEntry
{
    public required string InternalName { get; init; }

    public required string DisplayName { get; init; }

    public string IconBaseName { get; init; } = string.Empty;

    public bool IsSpectator { get; init; }

    public bool IsRandomSelector { get; init; }
}

public static class LobbySideCatalog
{
    public const string RandomInternalName = "Random";
    public const string SpectatorInternalName = "Spectator";

    public static IReadOnlyList<LobbySideEntry> Load(bool includeSpectator)
    {
        var entries = new List<LobbySideEntry>
        {
            new()
            {
                InternalName = RandomInternalName,
                DisplayName = RandomInternalName,
                IconBaseName = "random",
            },
        };

        string[] sides = ClientConfiguration.Instance.Sides
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string selector in LoadRandomSelectorNames())
        {
            entries.Add(new LobbySideEntry
            {
                InternalName = selector,
                DisplayName = selector,
                IconBaseName = selector,
                IsRandomSelector = true,
            });
        }

        foreach (string side in sides)
        {
            entries.Add(new LobbySideEntry
            {
                InternalName = side,
                DisplayName = side,
                IconBaseName = side,
            });
        }

        if (includeSpectator)
        {
            entries.Add(new LobbySideEntry
            {
                InternalName = SpectatorInternalName,
                DisplayName = SpectatorInternalName,
                IconBaseName = "spectator",
                IsSpectator = true,
            });
        }

        return entries;
    }

    private static IEnumerable<string> LoadRandomSelectorNames()
    {
        string path = SafePath.CombineFilePath(ProgramConstants.GamePath, "GameOptions.ini");
        if (!File.Exists(path))
            return [];

        var ini = new IniFile(path);
        List<string>? keys = ini.GetSectionKeys("RandomSelectors");
        if (keys == null)
            return [];

        int sideCount = ClientConfiguration.Instance.Sides
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;

        var names = new List<string>();
        foreach (string key in keys)
        {
            try
            {
                string[] tmp = ini.GetStringListValue("RandomSelectors", key, string.Empty);
                var sideIds = Array.ConvertAll(tmp, int.Parse).Where(id => id >= 0 && id < sideCount).ToList();
                if (sideIds.Count > 1)
                    names.Add(key);
            }
            catch (FormatException)
            {
            }
        }

        return names;
    }
}
