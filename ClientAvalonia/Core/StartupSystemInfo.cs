using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
internal static class StartupSystemInfo
{
    public static void LogHardwareSpecifications()
    {
        string cpu = "CPU info not found";
        string videoController = "Video controller info not found";
        string memory = "Memory info not found";

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            var parts = new List<string>();
            foreach (System.Management.ManagementObject proc in searcher.Get())
            {
                string? name = proc["Name"]?.ToString()?.Trim();
                object? cores = proc["NumberOfCores"];
                if (!string.IsNullOrEmpty(name))
                    parts.Add($"{name} ({cores} cores)");
            }

            if (parts.Count > 0)
                cpu = string.Join(' ', parts);
        }
        catch
        {
        }

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            var parts = new List<string>();
            foreach (System.Management.ManagementObject mo in searcher.Get())
            {
                if (mo.Properties["CurrentBitsPerPixel"]?.Value == null)
                    continue;

                string? description = mo.Properties["Description"]?.Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(description))
                    parts.Add("Video controller: " + description);
            }

            if (parts.Count > 0)
                videoController = string.Join(' ', parts);
        }
        catch
        {
        }

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("Select * From Win32_PhysicalMemory");
            ulong total = 0;
            foreach (System.Management.ManagementObject ram in searcher.Get())
                total += Convert.ToUInt64(ram.GetPropertyValue("Capacity"));

            if (total != 0)
                memory = "Total physical memory: " + (total >= 1073741824 ? total / 1073741824 + "GB" : total / 1048576 + "MB");
        }
        catch
        {
        }

        Logger.Log($"Hardware info: {cpu} | {videoController} | {memory}");
    }
}
