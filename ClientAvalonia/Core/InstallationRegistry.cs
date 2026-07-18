using ClientCore;
using Microsoft.Win32;
using Rampastring.Tools;
using System;
using System.IO;

namespace ClientAvalonia.Core;

/// <summary>
/// Reads/writes the MG install path under HKCU\SOFTWARE\MomentOfGenesis\InstallPath
/// (aligned with DXMainClient Startup.WriteInstallPathToRegistry).
/// </summary>
public static class InstallationRegistry
{
    public const string MgRegistryKeyName = "MomentOfGenesis";
    public const string MgGameExecutableName = "gamemd.exe";

    private const string InstallPathValueName = "InstallPath";

    /// <summary>Candidate keys for early bootstrap / repair. Main branch is MG-only.</summary>
    private static readonly string[] EarlyBoundCandidateKeys =
    {
        MgRegistryKeyName,
    };

    public static string RegistryKeyPath =>
        "SOFTWARE\\" + (ClientCoreBootstrap.IsInitialized
            ? ClientConfiguration.Instance.InstallationPathRegKey
            : MgRegistryKeyName);

    public static string MgRegistryKeyPath => "SOFTWARE\\" + MgRegistryKeyName;

    /// <summary>
    /// MG boot self-check:
    /// 1. No InstallPath → write <paramref name="launcherCwd"/> and use it.
    /// 2. InstallPath set → require directory + gamemd.exe; otherwise rewrite launcher CWD.
    /// </summary>
    public static string ResolveAndHealMgInstallPath(string launcherCwd)
    {
        string fallback = NormalizeRoot(launcherCwd);

        if (!OperatingSystem.IsWindows())
            return fallback;

        string? recorded = TryReadInstallPathFromKey(MgRegistryKeyPath);
        if (string.IsNullOrWhiteSpace(recorded))
        {
            TryWriteInstallPathToKey(MgRegistryKeyPath, fallback);
            Logger.Log($"InstallationRegistry: MG InstallPath missing — wrote '{fallback}'.");
            return fallback;
        }

        string resolved = NormalizeRoot(recorded);
        if (IsMgInstallPathValid(resolved))
        {
            Logger.Log($"InstallationRegistry: MG InstallPath valid -> '{resolved}'.");
            return resolved;
        }

        TryWriteInstallPathToKey(MgRegistryKeyPath, fallback);
        Logger.Log(
            $"InstallationRegistry: MG InstallPath invalid ('{resolved}') — rewrote '{fallback}'.");
        return fallback;
    }

    /// <summary>
    /// Valid MG install root: absolute path, directory exists, and contains <c>gamemd.exe</c>.
    /// </summary>
    public static bool IsMgInstallPathValid(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return false;

        string resolved = NormalizeRoot(installPath);

        try
        {
            if (!Path.IsPathRooted(resolved))
                return false;

            if (!Directory.Exists(resolved))
                return false;

            return File.Exists(Path.Combine(resolved, MgGameExecutableName));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Early-bootstrap install path lookup (MG key). When
    /// <paramref name="validateFilePresence"/> is true, requires <c>gamemd.exe</c>.
    /// </summary>
    public static string? TryReadEarlyBoundInstallPath(bool validateFilePresence = true)
        => TryReadEarlyBoundInstallPath(EarlyBoundCandidateKeys, validateFilePresence);

    /// <summary>Test seam: scan an explicit candidate key list instead of the hard-coded list.</summary>
    internal static string? TryReadEarlyBoundInstallPath(string[] candidateKeys, bool validateFilePresence = true)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        foreach (string candidate in candidateKeys)
        {
            string? path = TryReadInstallPathFromKey("SOFTWARE\\" + candidate);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string resolved = NormalizeRoot(path);

            if (validateFilePresence && !IsMgInstallPathValid(resolved))
            {
                Logger.Log(
                    $"InstallationRegistry: early-bound candidate '{candidate}' -> '{resolved}' rejected (no {MgGameExecutableName}).");
                continue;
            }

            Logger.Log($"InstallationRegistry: early-bound InstallPath resolved via '{candidate}' -> '{resolved}'.");
            return resolved;
        }

        return null;
    }

    /// <summary>Writes <paramref name="installPath"/> to the MG registry key (early-bound).</summary>
    public static void TryWriteEarlyBoundInstallPath(string installPath)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (string.IsNullOrWhiteSpace(installPath))
            return;

        TryWriteInstallPathToKey(MgRegistryKeyPath, NormalizeRoot(installPath));
    }

    /// <summary>
    /// Returns true when <paramref name="installPath"/> looks like a usable client root
    /// (Resources/ClientDefinitions.ini present). Used by general bootstrap helpers.
    /// </summary>
    public static bool IsInstallPathValid(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return false;

        string resolved = NormalizeRoot(installPath);

        try
        {
            if (!Path.IsPathRooted(resolved))
                return false;

            string clientDefs = Path.Combine(resolved, "Resources", "ClientDefinitions.ini");
            return File.Exists(clientDefs);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Repairs MG InstallPath against <paramref name="knownGoodRoot"/> using gamemd.exe rules.
    /// </summary>
    public static int TryRepairAllCandidates(string? knownGoodRoot)
        => TryRepairAllCandidates(EarlyBoundCandidateKeys, knownGoodRoot);

    /// <summary>Test seam: repair an explicit candidate key list instead of the hard-coded list.</summary>
    internal static int TryRepairAllCandidates(string[] candidateKeys, string? knownGoodRoot)
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        string? goodRoot = IsMgInstallPathValid(knownGoodRoot)
            ? NormalizeRoot(knownGoodRoot!)
            : null;

        int repaired = 0;

        foreach (string candidate in candidateKeys)
        {
            string keyPath = "SOFTWARE\\" + candidate;
            string? recorded = TryReadInstallPathFromKey(keyPath);

            if (string.IsNullOrWhiteSpace(recorded))
            {
                if (goodRoot != null)
                {
                    TryWriteInstallPathToKey(keyPath, goodRoot);
                    Logger.Log($"InstallationRegistry: repair wrote missing '{candidate}' -> '{goodRoot}'.");
                    repaired++;
                }

                continue;
            }

            if (IsMgInstallPathValid(recorded))
                continue;

            if (goodRoot != null)
            {
                TryWriteInstallPathToKey(keyPath, goodRoot);
                Logger.Log($"InstallationRegistry: repair rewrote stale '{candidate}' '{recorded}' -> '{goodRoot}'.");
            }
            else
            {
                TryClearInstallPathFromKey(keyPath);
                Logger.Log($"InstallationRegistry: repair cleared stale '{candidate}' (recorded='{recorded}', no verified root).");
            }

            repaired++;
        }

        if (repaired > 0)
            Logger.Log($"InstallationRegistry: repaired {repaired} registry key(s).");

        return repaired;
    }

    /// <summary>Configured-key write (post-bootstrap). Honors WritePathToRegistry INI toggle.</summary>
    public static void TryUpdateInstallPath(string installPath)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!ClientCoreBootstrap.IsInitialized)
            return;

        if (!UserINISettings.Instance.WritePathToRegistry)
        {
            Logger.Log("InstallationRegistry: skipping InstallPath write (WriteInstallationPathToRegistry=false).");
            return;
        }

        if (string.IsNullOrWhiteSpace(installPath))
            return;

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(InstallPathValueName, NormalizeRoot(installPath));
            Logger.Log($"InstallationRegistry: updated InstallPath to {installPath}.");
        }
        catch (Exception ex)
        {
            Logger.Log($"InstallationRegistry: failed to write InstallPath: {ex.Message}");
        }
    }

    /// <summary>Configured-key read (post-bootstrap).</summary>
    public static string? TryReadInstallPath()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        return TryReadInstallPathFromKey(RegistryKeyPath);
    }

    private static string NormalizeRoot(string path)
        => path.TrimEnd('\\', '/');

    private static string? TryReadInstallPathFromKey(string keyPath)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(InstallPathValueName)?.ToString();
        }
        catch (Exception ex)
        {
            Logger.Log($"InstallationRegistry: failed to read InstallPath from {keyPath}: {ex.Message}");
            return null;
        }
    }

    private static void TryWriteInstallPathToKey(string keyPath, string normalizedPath)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue(InstallPathValueName, normalizedPath);
        }
        catch (Exception ex)
        {
            Logger.Log($"InstallationRegistry: failed to write InstallPath to {keyPath}: {ex.Message}");
        }
    }

    private static void TryClearInstallPathFromKey(string keyPath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key == null)
                return;

            if (key.GetValue(InstallPathValueName) != null)
                key.DeleteValue(InstallPathValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Logger.Log($"InstallationRegistry: failed to clear InstallPath at {keyPath}: {ex.Message}");
        }
    }
}
