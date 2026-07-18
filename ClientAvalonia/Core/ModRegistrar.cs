using System;
using System.Collections.Generic;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>
/// Registers a single Avalonia workspace key
/// (<c>SOFTWARE\ClientAvalonia\ModWorkspaces\{ModName}\InstallPath</c>).
/// Never batch-writes candidates; never writes DX launcher keys except explicit orphan cleanup.
/// </summary>
public static class ModRegistrar
{
    public static bool TryRegister(string modName, string installPath, string clientGameType, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(modName))
        {
            error = "Mod 名称不能为空。";
            return false;
        }

        if (!ModWorkspaceRegistry.IsInstallPathValid(installPath))
        {
            error = "路径无效：目录下必须存在 Resources\\ClientDefinitions.ini。";
            return false;
        }

        string? normalizedType = ModWorkspaceRegistry.NormalizeClientGameType(clientGameType);
        if (normalizedType == null)
        {
            error = $"请选择 ClientGameType（可选：{string.Join(", ", ModWorkspaceRegistry.ClientGameTypeOptions)}）。";
            return false;
        }

        string name = modName.Trim();
        if (!ModWorkspaceRegistry.TryWriteInstallPath(name, installPath, out error))
            return false;

        if (!ModWorkspaceRegistry.TryWriteClientGameType(name, normalizedType, out error))
            return false;

        Logger.Log(
            $"ModRegistrar: registered Avalonia workspace '{name}' -> '{installPath}' (ClientGameType={normalizedType}).");
        return true;
    }

    public static bool TryClear(string modName, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(modName))
        {
            error = "Mod 名称不能为空。";
            return false;
        }

        if (!ModWorkspaceRegistry.TryClearInstallPath(modName.Trim(), out error))
            return false;

        Logger.Log($"ModRegistrar: cleared Avalonia workspace '{modName}'.");
        return true;
    }

    /// <summary>
    /// Clears legacy DX <c>SOFTWARE\{ModName}\InstallPath</c> values that neither DX nor Avalonia
    /// should keep:
    /// <list type="bullet">
    /// <item>Stale / missing ClientDefinitions (unusable)</item>
    /// <item>Valid path whose ClientDefinitions suggests a <b>different</b> ModName
    /// (pollution from old multi-key repair, e.g. YR→MG folder)</item>
    /// </list>
    /// Keeps keys where <c>SuggestModName(path) == ModName</c> (legitimate DX ownership).
    /// Never modifies Avalonia <see cref="ModWorkspaceRegistry"/> keys.
    /// </summary>
    public static int TryCleanupOrphanLegacyDxKeys(
        IReadOnlyList<string>? candidateKeys = null,
        List<string>? clearedModNames = null)
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        int cleared = 0;
        IReadOnlyList<string> keys = candidateKeys ?? ModWorkspaceRegistry.DefaultCandidateModNames;

        foreach (string modName in keys)
        {
            if (string.IsNullOrWhiteSpace(modName))
                continue;

            string name = modName.Trim();
            string? path = ModWorkspaceRegistry.TryReadLegacyDxInstallPath(name);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            bool orphan;
            if (!ModWorkspaceRegistry.IsInstallPathValid(path))
            {
                orphan = true;
                Logger.Log($"ModRegistrar: DX '{name}' is stale ('{path}') — clearing.");
            }
            else
            {
                string suggested = ModRegistryCatalog.SuggestModName(path);
                orphan = !suggested.Equals(name, StringComparison.OrdinalIgnoreCase);
                if (orphan)
                {
                    Logger.Log(
                        $"ModRegistrar: DX '{name}' points at '{path}' but ClientDefinitions suggests '{suggested}' — clearing orphan.");
                }
            }

            if (!orphan)
                continue;

            if (!ModWorkspaceRegistry.TryClearLegacyDxInstallPath(name, out _))
                continue;

            cleared++;
            clearedModNames?.Add(name);
        }

        if (cleared > 0)
            Logger.Log($"ModRegistrar: cleaned {cleared} orphan legacy DX InstallPath value(s).");

        return cleared;
    }
}
