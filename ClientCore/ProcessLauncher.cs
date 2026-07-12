using System.Diagnostics;
using Rampastring.Tools;

namespace ClientCore
{
    public static class ProcessLauncher
    {
        public static void StartShellProcess(string commandLine, string arguments = null)
        {
            // MG: ChangelogURL/credits/support URLs are empty in ClientDefinitions.ini.
            // Process.Start with empty FileName throws on Chinese Windows
            // ("尚未提供文件名..."). Guard here to align with DX's intent without
            // crashing when the URL is unconfigured.
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                Logger.Log("ProcessLauncher: skipped shell launch — commandLine is empty.");
                return;
            }

            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = commandLine,
                Arguments = arguments,
                UseShellExecute = true
            });
        }
    }
}
