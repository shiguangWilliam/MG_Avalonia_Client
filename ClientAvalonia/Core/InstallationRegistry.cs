using ClientCore;
using Microsoft.Win32;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.IO;

namespace ClientAvalonia.Core;

/// <summary>
/// Reads/writes the game install path under HKCU\SOFTWARE\{InstallationPathRegKey}\InstallPath
/// (aligned with DXMainClient Startup.WriteInstallPathToRegistry).
/// </summary>
/// <remarks>
/// Two access modes:
///  - <b>Configured</b>: single key from <see cref="ClientConfiguration.InstallationPathRegKey"/>
///    (requires ClientCore bootstrap to have loaded ClientDefinitions.ini).
///  - <b>Early-bound</b>: scans a hard-coded candidate list of registry keys. Used during
///    bootstrap BEFORE ClientDefinitions.ini is located, since the configured key name itself
///    comes from that INI. Without this, a launcher that starts the client with an unrelated
///    CWD (e.g. C:\Windows\System32) makes FindGameRoot fall back to that CWD and the very
///    next call to <c>ClientConfiguration.Instance</c> throws FileNotFoundException.
/// </remarks>
public static class InstallationRegistry
{
    private const string InstallPathValueName = "InstallPath";

    /// <summary>
    /// Candidate registry key names probed during early bootstrap. Includes the configured
    /// keys for DTA, MG, and other known CnCNet mods so the same client binary can relocate
    /// any of them when CWD is unreliable.
    /// </summary>
    private static readonly string[] EarlyBoundCandidateKeys =
    {
        "MomentOfGenesis", // MG (default for this build)
        "TiberianSun",     // DTA / TS default (ClientConfiguration fallback)
        "CnCNet",
        "YR",
        "MentalOmega",
        "TwistedInsurrection",
    };

    public static string RegistryKeyPath =>
        "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey;

    /// <summary>
    /// Early-bootstrap install path lookup. Scans the candidate registry keys and returns the
    /// first <c>InstallPath</c> value that points to a directory containing
    /// <c>Resources/ClientDefinitions.ini</c>. Returns null on non-Windows or when no valid
    /// install path is recorded.
    /// </summary>
    /// <param name="validateFilePresence">
    /// When true (default), only returns paths whose <c>Resources/ClientDefinitions.ini</c>
    /// exists — preventing stale registry entries from hijacking the bootstrap.
    /// </param>
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

            string resolved = path.TrimEnd('\\', '/');

            if (validateFilePresence)
            {
                string clientDefs = Path.Combine(resolved, "Resources", "ClientDefinitions.ini");
                if (!File.Exists(clientDefs))
                {
                    Logger.Log($"InstallationRegistry: early-bound candidate '{candidate}' -> '{resolved}' rejected (no ClientDefinitions.ini).");
                    continue;
                }
            }

            Logger.Log($"InstallationRegistry: early-bound InstallPath resolved via '{candidate}' -> '{resolved}'.");
            return resolved;
        }

        return null;
    }

    /// <summary>
    /// Writes <paramref name="installPath"/> to <b>all</b> candidate registry keys (early-bound).
    /// Used during bootstrap right after the game root is found, so subsequent launches can
    /// relocate the install regardless of CWD. Does NOT depend on ClientConfiguration being
    /// initialized yet. Once ClientCore is bootstrapped, the configured key is also updated.
    /// </summary>
    public static void TryWriteEarlyBoundInstallPath(string installPath)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (string.IsNullOrWhiteSpace(installPath))
            return;

        string normalized = installPath.TrimEnd('\\', '/');

        foreach (string candidate in EarlyBoundCandidateKeys)
        {
            TryWriteInstallPathToKey("SOFTWARE\\" + candidate, normalized);
        }
    }

    /// <summary>
    /// Returns true when <paramref name="installPath"/> looks like a real install root:
    /// non-empty, absolute, and containing <c>Resources/ClientDefinitions.ini</c>.
    /// </summary>
    public static bool IsInstallPathValid(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return false;

        string resolved = installPath.TrimEnd('\\', '/');

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
    /// Scans every candidate registry key, validates the recorded <c>InstallPath</c>, and
    /// overwrites (or clears) any stale/wrong entry with <paramref name="knownGoodRoot"/>.
    /// Called from bootstrap after a trustworthy root has been discovered so the registry
    /// self-heals instead of silently carrying bad data across launches.
    /// </summary>
    /// <param name="knownGoodRoot">
    /// The freshly-resolved install root. When null/invalid, bad entries are only cleared
    /// (no overwrite), so we never propagate an unverified path into the registry.
    /// </param>
    /// <returns>Number of registry keys that were repaired.</returns>
    public static int TryRepairAllCandidates(string? knownGoodRoot)
        => TryRepairAllCandidates(EarlyBoundCandidateKeys, knownGoodRoot);

    /// <summary>Test seam: repair an explicit candidate key list instead of the hard-coded list.</summary>
    internal static int TryRepairAllCandidates(string[] candidateKeys, string? knownGoodRoot)
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        string? goodRoot = IsInstallPathValid(knownGoodRoot)
            ? knownGoodRoot!.TrimEnd('\\', '/')
            : null;

        int repaired = 0;

        foreach (string candidate in candidateKeys)
        {
            string keyPath = "SOFTWARE\\" + candidate;
            string? recorded = TryReadInstallPathFromKey(keyPath);

            // No entry: write it if we have a good root (first launch on this key name).
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

            if (IsInstallPathValid(recorded))
                continue;

            // Stale entry detected: either rewrite with the good root or, if we don't have
            // a verified root either, delete the bad value so it can't hijack later launches.
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
            key.SetValue(InstallPathValueName, installPath.TrimEnd('\\', '/'));
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

            // Drop the value only; keep the key itself (other tools may store sibling values).
            if (key.GetValue(InstallPathValueName) != null)
                key.DeleteValue(InstallPathValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Logger.Log($"InstallationRegistry: failed to clear InstallPath at {keyPath}: {ex.Message}");
        }
    }
}
