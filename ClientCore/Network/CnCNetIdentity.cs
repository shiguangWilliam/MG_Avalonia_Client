using System;
using Rampastring.Tools;

namespace ClientCore.Network;

/// <summary>
/// Stable IRC USER ident fragment (aligned with DXMainClient Connection.SetId / Startup.cs registry Ident).
/// </summary>
public static class CnCNetIdentity
{
    private const int IdLength = 9;

    public static void EnsurePersisted()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey);

            if (key.GetValue("Ident") != null)
                return;

            string ident = Environment.MachineName + Environment.UserName + Random.Shared.Next(int.MaxValue - 1);
            key.SetValue("Ident", ident);
            Logger.Log("CnCNetIdentity: persisted new registry Ident.");
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetIdentity: failed to persist Ident: {ex.Message}");
        }
    }

    public static string CreateSystemId()
    {
        EnsurePersisted();

        string raw = TryLoadPersistedIdent() ?? Environment.MachineName + Environment.UserName;
        int maxLength = IdLength - (ClientConfiguration.Instance.LocalGame.Length + 1);
        maxLength = Math.Max(1, maxLength);
        string hash = Utilities.CalculateSHA1ForString(raw);
        return hash[..Math.Min(hash.Length, maxLength)];
    }

    private static string? TryLoadPersistedIdent()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey);
            return key?.GetValue("Ident")?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
