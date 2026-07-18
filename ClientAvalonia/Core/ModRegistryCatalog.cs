using System.Collections.Generic;
using System.IO;
using ClientAvalonia.IniUi.Loading;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>
/// Enumerates Avalonia multi-mod workspace candidates (Ready / Stale / Missing).
/// Does not silently first-hit. Does not write DX registry keys.
/// </summary>
public static class ModRegistryCatalog
{
    public static IReadOnlyList<string> DefaultCandidateKeys => ModWorkspaceRegistry.KnownDxHintModNames;

    /// <summary>
    /// Enumerate workspaces for the picker.
    /// Ready/Stale entries are <b>deduped by InstallPath</b> so polluted DX keys that all
    /// point at the same MG folder do not produce six identical「创世之刻」rows.
    /// </summary>
    public static IReadOnlyList<ModRegistryEntry> Enumerate(
        IReadOnlyList<string>? candidateKeys = null,
        bool includeLegacyDxHints = true,
        bool includeMissingSlots = false)
    {
        IReadOnlyList<string> modNames = ModDiscoveryCatalog.BuildModNamesToProbe(candidateKeys, includeLegacyDxHints);

        // Path → best entry (Avalonia wins over DX hint).
        var byPath = new Dictionary<string, ModRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<ModRegistryEntry>();

        foreach (string modName in modNames)
        {
            string? avaloniaPath = ModWorkspaceRegistry.TryReadInstallPath(modName);
            if (!string.IsNullOrWhiteSpace(avaloniaPath))
            {
                ConsiderPath(byPath, modName, avaloniaPath, ModRegistryEntrySource.AvaloniaRegistry);
                continue;
            }

            if (includeLegacyDxHints)
            {
                string? dxPath = ModWorkspaceRegistry.TryReadLegacyDxInstallPath(modName);
                if (!string.IsNullOrWhiteSpace(dxPath))
                {
                    // Prefer the ModName suggested by that install's ClientDefinitions,
                    // not the polluted DX key name (e.g. YR/CnCNet → still MomentOfGenesis).
                    string suggested = SuggestModName(dxPath);
                    ConsiderPath(byPath, suggested, dxPath, ModRegistryEntrySource.LegacyDxHint);
                    continue;
                }
            }

            if (includeMissingSlots && candidateKeys != null)
                missing.Add(new ModRegistryEntry(modName, null, ModRegistryEntryState.Missing));
        }

        var list = new List<ModRegistryEntry>(byPath.Count + missing.Count);
        list.AddRange(byPath.Values);
        list.Sort(CompareEntries);
        list.AddRange(missing);
        return list;
    }

    public static IReadOnlyList<ModRegistryEntry> EnumerateReady(
        IReadOnlyList<string>? candidateKeys = null,
        bool includeLegacyDxHints = true)
    {
        var ready = new List<ModRegistryEntry>();
        foreach (ModRegistryEntry entry in Enumerate(candidateKeys, includeLegacyDxHints, includeMissingSlots: false))
        {
            if (entry.IsReady)
                ready.Add(entry);
        }

        return ready;
    }

    /// <summary>Probe CWD / exe walk-up (no registry first-hit).</summary>
    public static string? TryProbeLocalGameRoot(string? startDirectory = null)
    {
        startDirectory ??= Directory.GetCurrentDirectory();
        return ClientEnvironment.TryFindGameRootWithoutRegistry(startDirectory);
    }

    /// <summary>Suggest ModName from ClientDefinitions RegistryInstallPath, else LocalGame, else folder name.</summary>
    public static string SuggestModName(string gameRoot)
    {
        try
        {
            string defs = Path.Combine(gameRoot, "Resources", "ClientDefinitions.ini");
            if (!File.Exists(defs))
                return Path.GetFileName(gameRoot.TrimEnd('\\', '/')) ?? "Unknown";

            var ini = new IniFile(defs);
            string? registryKey = ini.GetStringValue("Settings", "RegistryInstallPath", string.Empty);
            if (!string.IsNullOrWhiteSpace(registryKey))
                return registryKey.Trim();

            string? localGame = ini.GetStringValue("Settings", "LocalGame", string.Empty);
            if (!string.IsNullOrWhiteSpace(localGame))
                return localGame.Trim();
        }
        catch (Exception ex)
        {
            Logger.Log($"ModRegistryCatalog: SuggestModName failed: {ex.Message}");
        }

        return Path.GetFileName(gameRoot.TrimEnd('\\', '/')) ?? "Unknown";
    }

    /// <summary>Read ClientGameType from ClientDefinitions.ini if present.</summary>
    public static string? TryReadClientGameTypeFromDefinitions(string gameRoot)
    {
        try
        {
            string defs = Path.Combine(gameRoot, "Resources", "ClientDefinitions.ini");
            if (!File.Exists(defs))
                return null;

            var ini = new IniFile(defs);
            string? value = ini.GetStringValue("Settings", "ClientGameType", string.Empty);
            return ModWorkspaceRegistry.NormalizeClientGameType(value);
        }
        catch (Exception ex)
        {
            Logger.Log($"ModRegistryCatalog: TryReadClientGameTypeFromDefinitions failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolve ClientGameType for picker/bind: ini → Avalonia registry → null (caller must pick).
    /// </summary>
    public static string? ResolveClientGameTypeHint(string? modName, string? installPath)
    {
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            string? fromIni = TryReadClientGameTypeFromDefinitions(installPath);
            if (fromIni != null)
                return fromIni;
        }

        if (!string.IsNullOrWhiteSpace(modName))
        {
            string? fromRegistry = ModWorkspaceRegistry.NormalizeClientGameType(
                ModWorkspaceRegistry.TryReadClientGameType(modName));
            if (fromRegistry != null)
                return fromRegistry;
        }

        return null;
    }

    public static ModRegistryEntry CreateManualEntry(string modName, string installPath)
    {
        string normalized = installPath.TrimEnd('\\', '/');
        ModRegistryEntryState state = ModWorkspaceRegistry.IsInstallPathValid(normalized)
            ? ModRegistryEntryState.Ready
            : ModRegistryEntryState.Stale;
        return new ModRegistryEntry(
            modName.Trim(),
            normalized,
            state,
            ModRegistryEntrySource.Manual,
            TryReadDisplayName(normalized) ?? modName);
    }

    private static void ConsiderPath(
        Dictionary<string, ModRegistryEntry> byPath,
        string modName,
        string installPath,
        ModRegistryEntrySource source)
    {
        string normalized = installPath.TrimEnd('\\', '/');
        ModRegistryEntryState state = ModWorkspaceRegistry.IsInstallPathValid(normalized)
            ? ModRegistryEntryState.Ready
            : ModRegistryEntryState.Stale;
        var candidate = new ModRegistryEntry(
            modName.Trim(),
            normalized,
            state,
            source,
            TryReadDisplayName(normalized) ?? modName);

        if (!byPath.TryGetValue(normalized, out ModRegistryEntry? existing))
        {
            byPath[normalized] = candidate;
            return;
        }

        // Prefer Avalonia registry over DX hints; otherwise keep the first.
        if (existing.Source != ModRegistryEntrySource.AvaloniaRegistry
            && source == ModRegistryEntrySource.AvaloniaRegistry)
        {
            byPath[normalized] = candidate;
        }
    }

    private static int CompareEntries(ModRegistryEntry a, ModRegistryEntry b)
    {
        int readyCmp = (b.IsReady ? 1 : 0).CompareTo(a.IsReady ? 1 : 0);
        if (readyCmp != 0)
            return readyCmp;

        int sourceCmp = SourceRank(a.Source).CompareTo(SourceRank(b.Source));
        if (sourceCmp != 0)
            return sourceCmp;

        return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static int SourceRank(ModRegistryEntrySource source) => source switch
    {
        ModRegistryEntrySource.AvaloniaRegistry => 0,
        ModRegistryEntrySource.Manual => 1,
        _ => 2,
    };

    private static string? TryReadDisplayName(string gameRoot)
    {
        try
        {
            string defs = Path.Combine(gameRoot, "Resources", "ClientDefinitions.ini");
            if (!File.Exists(defs))
                return null;

            var ini = new IniFile(defs);
            string? longName = ini.GetStringValue("Settings", "LongGameName", string.Empty);
            if (!string.IsNullOrWhiteSpace(longName))
                return longName.Trim();

            string? windowTitle = ini.GetStringValue("Settings", "WindowTitle", string.Empty);
            if (!string.IsNullOrWhiteSpace(windowTitle))
                return windowTitle.Trim();

            string? localGame = ini.GetStringValue("Settings", "LocalGame", string.Empty);
            return string.IsNullOrWhiteSpace(localGame) ? null : localGame.Trim();
        }
        catch
        {
            return null;
        }
    }
}
