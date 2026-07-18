using Microsoft.Win32;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.IO;

namespace ClientAvalonia.Core;

/// <summary>
/// Avalonia multi-mod workspace registry — <b>separate</b> from DX
/// <c>HKCU\SOFTWARE\{RegistryInstallPath}\InstallPath</c>.
/// </summary>
/// <remarks>
/// Contract:
/// <code>
/// Hive:  HKEY_CURRENT_USER
/// Key:   SOFTWARE\ClientAvalonia\ModWorkspaces\{ModName}
/// Value: InstallPath (REG_SZ) = absolute game root (no trailing slash)
/// Value: ClientGameType (REG_SZ) = TS | YR | Ares | RA (optional companion)
/// </code>
/// Never writes DX launcher keys. Optional read-only probes of DX keys are elsewhere
/// (<see cref="ModRegistryCatalog"/>) for import hints only.
/// </remarks>
public static class ModWorkspaceRegistry
{
    public const string RootKeyPath = @"SOFTWARE\ClientAvalonia\ModWorkspaces";
    public const string InstallPathValueName = "InstallPath";
    public const string ClientGameTypeValueName = "ClientGameType";

    /// <summary>Allowed ClientGameType labels for the picker (matches ClientType enum names).</summary>
    public static readonly string[] ClientGameTypeOptions = ["TS", "YR", "Ares", "RA"];

    /// <summary>
    /// Optional read-only probes of legacy DX <c>SOFTWARE\{ModName}\InstallPath</c> keys.
    /// Not an authoritative mod list — Avalonia registry + user browse/register are primary.
    /// </summary>
    public static readonly string[] KnownDxHintModNames =
    {
        "MomentOfGenesis",
        "TiberianSun",
        "CnCNet",
        "YR",
        "MentalOmega",
        "TwistedInsurrection",
        "lnod",
    };

    /// <summary>Alias for tests / legacy callers.</summary>
    public static readonly string[] DefaultCandidateModNames = KnownDxHintModNames;

    public static string KeyPathFor(string modName)
        => RootKeyPath + "\\" + modName.Trim();

    public static bool IsInstallPathValid(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return false;

        string resolved = installPath.TrimEnd('\\', '/');

        try
        {
            if (!Path.IsPathRooted(resolved))
                return false;

            return File.Exists(Path.Combine(resolved, "Resources", "ClientDefinitions.ini"));
        }
        catch
        {
            return false;
        }
    }

    public static string? TryReadInstallPath(string modName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(modName))
            return null;

        return TryReadValue(KeyPathFor(modName), InstallPathValueName);
    }

    public static bool TryWriteInstallPath(string modName, string installPath, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            error = "注册表写入仅支持 Windows。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(modName))
        {
            error = "Mod 名称不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(installPath))
        {
            error = "安装路径不能为空。";
            return false;
        }

        string normalized = installPath.TrimEnd('\\', '/');
        if (!TryWriteValue(KeyPathFor(modName), InstallPathValueName, normalized))
        {
            error = "写入 Avalonia 工作区注册表失败（详见日志）。";
            return false;
        }

        Logger.Log($"ModWorkspaceRegistry: wrote '{modName}' -> '{normalized}'.");
        return true;
    }

    public static string? TryReadClientGameType(string modName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(modName))
            return null;

        // Do not reuse InstallPath reader (it TrimEnds path separators).
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPathFor(modName));
            string? raw = key?.GetValue(ClientGameTypeValueName)?.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }
        catch (Exception ex)
        {
            Logger.Log($"ModWorkspaceRegistry: read ClientGameType failed for '{modName}': {ex.Message}");
            return null;
        }
    }

    public static bool TryWriteClientGameType(string modName, string clientGameType, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            error = "注册表写入仅支持 Windows。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(modName))
        {
            error = "Mod 名称不能为空。";
            return false;
        }

        if (!IsKnownClientGameType(clientGameType))
        {
            error = $"ClientGameType 无效（可选：{string.Join(", ", ClientGameTypeOptions)}）。";
            return false;
        }

        if (!TryWriteValue(KeyPathFor(modName), ClientGameTypeValueName, clientGameType.Trim()))
        {
            error = "写入 Avalonia ClientGameType 失败（详见日志）。";
            return false;
        }

        Logger.Log($"ModWorkspaceRegistry: wrote ClientGameType '{modName}' = '{clientGameType.Trim()}'.");
        return true;
    }

    public static bool IsKnownClientGameType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (string option in ClientGameTypeOptions)
        {
            if (option.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Normalize to canonical casing (TS/YR/Ares/RA).</summary>
    public static string? NormalizeClientGameType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        foreach (string option in ClientGameTypeOptions)
        {
            if (option.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
                return option;
        }

        return null;
    }

    public static bool TryClearInstallPath(string modName, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            error = "注册表清除仅支持 Windows。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(modName))
        {
            error = "Mod 名称不能为空。";
            return false;
        }

        string keyPath = KeyPathFor(modName);
        if (!TryDeleteValue(keyPath, InstallPathValueName))
        {
            error = "清除 Avalonia 工作区注册表失败（详见日志）。";
            return false;
        }

        // Best-effort: clear companion ClientGameType so the subkey does not linger half-stale.
        TryDeleteValue(keyPath, ClientGameTypeValueName);

        Logger.Log($"ModWorkspaceRegistry: cleared InstallPath for '{modName}'.");
        return true;
    }

    /// <summary>All ModName subkeys currently present under the Avalonia workspace root.</summary>
    public static IReadOnlyList<string> ListRegisteredModNames()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<string>();

        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(RootKeyPath);
            if (root == null)
                return Array.Empty<string>();

            string[] names = root.GetSubKeyNames();
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return names;
        }
        catch (Exception ex)
        {
            Logger.Log($"ModWorkspaceRegistry: ListRegisteredModNames failed: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Read-only probe of a legacy DX key <c>SOFTWARE\{modName}\InstallPath</c>.
    /// Never writes. Used only as an import hint for the picker.
    /// </summary>
    public static string? TryReadLegacyDxInstallPath(string modName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(modName))
            return null;

        return TryReadValue("SOFTWARE\\" + modName.Trim(), InstallPathValueName);
    }

    /// <summary>
    /// Clears only the <c>InstallPath</c> value under a legacy DX key
    /// (<c>SOFTWARE\{modName}</c>). Does not delete the key or sibling values.
    /// </summary>
    public static bool TryClearLegacyDxInstallPath(string modName, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            error = "注册表清除仅支持 Windows。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(modName))
        {
            error = "Mod 名称不能为空。";
            return false;
        }

        if (!TryDeleteValue("SOFTWARE\\" + modName.Trim(), InstallPathValueName))
        {
            error = "清除 DX InstallPath 失败（详见日志）。";
            return false;
        }

        Logger.Log($"ModWorkspaceRegistry: cleared orphan DX InstallPath for '{modName}'.");
        return true;
    }

    private static string? TryReadValue(string keyPath, string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath);
            string? raw = key?.GetValue(valueName)?.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            return raw.TrimEnd('\\', '/');
        }
        catch (Exception ex)
        {
            Logger.Log($"ModWorkspaceRegistry: read failed at {keyPath}: {ex.Message}");
            return null;
        }
    }

    private static bool TryWriteValue(string keyPath, string valueName, string value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue(valueName, value);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"ModWorkspaceRegistry: write failed at {keyPath}: {ex.Message}");
            return false;
        }
    }

    private static bool TryDeleteValue(string keyPath, string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key == null)
                return true;

            if (key.GetValue(valueName) != null)
                key.DeleteValue(valueName, throwOnMissingValue: false);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"ModWorkspaceRegistry: clear failed at {keyPath}: {ex.Message}");
            return false;
        }
    }
}
