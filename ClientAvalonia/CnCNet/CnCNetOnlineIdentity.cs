using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using ClientCore;
using Microsoft.Win32;
using Rampastring.Tools;

#if NET8_0_OR_GREATER
using System.DirectoryServices;
using System.Management;
using System.Runtime.Versioning;
#endif

namespace ClientAvalonia.CnCNet;

/// <summary>
/// Online play machine identity (DXMainClient <c>Startup.GenerateOnlineId</c> + registry Ident).
/// </summary>
public static class CnCNetOnlineIdentity
{
    public static void GenerateAndPersist()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            GenerateAndPersistUnix();
            return;
        }

        GenerateAndPersistWindows();
    }

#if NET8_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    private static void GenerateAndPersistWindows()
    {
        try
        {
            string cpuid = string.Empty;
            using (var mbs = new ManagementObjectSearcher("Select * From Win32_processor"))
            {
                foreach (ManagementObject mo in mbs.Get())
                    cpuid = mo["ProcessorID"]?.ToString() ?? string.Empty;
            }

            string mbid = string.Empty;
            using (var mos = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard"))
            {
                foreach (ManagementObject mo in mos.Get())
                    mbid = mo["SerialNumber"]?.ToString() ?? string.Empty;
            }

            string sid = new SecurityIdentifier(
                (byte[])new DirectoryEntry($"WinNT://{Environment.MachineName},Computer")
                    .Children.Cast<DirectoryEntry>()
                    .First()
                    .InvokeGet("objectSID")!,
                0).AccountDomainSid!.Value;

            string ident = cpuid + mbid + sid;
            PersistIdent(ident);
        }
        catch (Exception)
        {
            var random = new Random();
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey);

            string str = random.Next(int.MaxValue - 1).ToString();
            try
            {
                object? existing = key.GetValue("Ident");
                if (existing == null)
                    key.SetValue("Ident", str);
                else
                    str = existing.ToString() ?? str;
            }
            catch
            {
            }

            PersistIdent(str);
        }
    }

    private static void GenerateAndPersistUnix()
    {
        try
        {
            string machineId = File.ReadAllText("/var/lib/dbus/machine-id").Trim();
            PersistIdent(machineId);
        }
        catch (Exception)
        {
            PersistIdent(new Random().Next(int.MaxValue - 1).ToString());
        }
    }

    private static void PersistIdent(string ident)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey);
                key.SetValue("Ident", ident);
            }
            catch (Exception ex)
            {
                Logger.Log($"CnCNetOnlineIdentity: registry write failed: {ex.Message}");
            }
        }

        Logger.Log("CnCNetOnlineIdentity: online identity initialized.");
    }
}
