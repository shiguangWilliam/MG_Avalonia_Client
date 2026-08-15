using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using ClientCore;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Core;

/// <summary>DXMainClient <c>PreStartup.CheckPermissions</c> (Avalonia: log + exit, no WinForms prompt).</summary>
internal static class ClientPermissions
{
    [SupportedOSPlatform("windows")]
    public static void EnsureWritableGameDirectory()
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (UserHasDirectoryAccessRights(AppState.Environment.GamePath, FileSystemRights.Modify))
            return;

        Logger.Log(
            "The client appears to be running from a write-protected directory. "
            + "Run from a writable folder or as administrator if the game is under Program Files.");

        Environment.Exit(1);
    }

    [SupportedOSPlatform("windows")]
    private static bool UserHasDirectoryAccessRights(string path, FileSystemRights accessRights)
    {
        var currentUser = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(currentUser);

        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            string progfiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progfilesx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (AppState.Environment.GamePath.Contains(progfiles, StringComparison.OrdinalIgnoreCase)
                || AppState.Environment.GamePath.Contains(progfilesx86, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        bool isInRoleWithAccess = false;

        try
        {
            var di = new DirectoryInfo(path);
            var acl = di.GetAccessControl();
            foreach (AuthorizationRule rule in acl.GetAccessRules(true, true, typeof(NTAccount)))
            {
                if (rule is not FileSystemAccessRule fsAccessRule)
                    continue;

                if ((fsAccessRule.FileSystemRights & accessRights) <= 0)
                    continue;

                if (rule.IdentityReference is not NTAccount ntAccount)
                    continue;

                try
                {
                    if (!principal.IsInRole(ntAccount.Value))
                        continue;

                    if (fsAccessRule.AccessControlType == AccessControlType.Deny)
                        return false;

                    isInRoleWithAccess = true;
                }
                catch (System.Security.SecurityException)
                {
                    continue;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return isInRoleWithAccess;
    }
}
