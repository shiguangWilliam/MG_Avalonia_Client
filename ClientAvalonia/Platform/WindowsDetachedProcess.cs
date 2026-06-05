using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClientAvalonia.Platform;

/// <summary>
/// Starts a child process in a new process group so console/control signals
/// (and some launcher wrappers) do not tear down the Avalonia client.
/// </summary>
internal static class WindowsDetachedProcess
{
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateBreakawayFromJob = 0x01000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static Process Start(ProcessStartInfo startInfo)
    {
        var si = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        var commandLine = new StringBuilder(BuildCommandLine(startInfo.FileName, startInfo.Arguments));

        uint flags = CreateUnicodeEnvironment | CreateNewProcessGroup | CreateBreakawayFromJob;

        if (!CreateProcessW(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                flags,
                IntPtr.Zero,
                string.IsNullOrWhiteSpace(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory,
                ref si,
                out ProcessInformation pi))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            return Process.GetProcessById(pi.dwProcessId);
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero)
                CloseHandle(pi.hThread);

            if (pi.hProcess != IntPtr.Zero)
                CloseHandle(pi.hProcess);
        }
    }

    private static string BuildCommandLine(string fileName, string arguments)
    {
        string quoted = QuoteIfNeeded(fileName);
        return string.IsNullOrWhiteSpace(arguments) ? quoted : $"{quoted} {arguments}";
    }

    private static string QuoteIfNeeded(string path)
    {
        if (path.Contains(' ') && !path.StartsWith('"'))
            return $"\"{path}\"";

        return path;
    }
}
