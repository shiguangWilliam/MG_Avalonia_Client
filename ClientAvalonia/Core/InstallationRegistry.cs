using ClientCore;
using Microsoft.Win32;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>
/// Reads/writes the game install path under HKCU\SOFTWARE\{InstallationPathRegKey}\InstallPath
/// (aligned with DXMainClient Startup.WriteInstallPathToRegistry).
/// </summary>
public static class InstallationRegistry
{
    private const string InstallPathValueName = "InstallPath";

    public static string RegistryKeyPath =>
        "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey;

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

    public static string? TryReadInstallPath()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            return key?.GetValue(InstallPathValueName)?.ToString();
        }
        catch (Exception ex)
        {
            Logger.Log($"InstallationRegistry: failed to read InstallPath: {ex.Message}");
            return null;
        }
    }
}
