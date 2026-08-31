using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ClientAvalonia.CnCNet;
using ClientCore;
using ClientCore.Enums;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Core;

/// <summary>Background work from DXMainClient <c>Startup.Execute</c> (threads vs tasks per DX comments).</summary>
internal static class StartupBackgroundTasks
{
    public static void StartHardwareProbe()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        // DX: WMI query is slow — dedicated thread, not Task.
        var thread = new Thread(StartupSystemInfo.LogHardwareSpecifications) { IsBackground = true };
        thread.Start();
    }

    public static void StartOnlineIdentityGeneration()
    {
        // DX: Task.Run causes crashes on Wine; use Thread for GenerateOnlineId.
        var thread = new Thread(CnCNetOnlineIdentity.GenerateAndPersist) { IsBackground = true };
        thread.Start();
    }

    public static void ScheduleLogMigration()
        => Task.Run(MigrateOldLogFiles);

    public static void ScheduleDebugFolderPrune()
    {
        if (AppState.Configuration.Legacy.ClientGameType != ClientType.Ares)
            return;

        Task.Run(() => PruneFiles(SafePath.GetDirectory(AppState.Environment.GamePath, "debug"), DateTime.Now.AddDays(-7)));
    }

    private static void MigrateOldLogFiles()
    {
        MigrateLogFiles(SafePath.GetDirectory(ProgramConstants.ClientUserFilesPath, "ClientCrashLogs"), "ClientCrashLog*.txt");
        MigrateLogFiles(SafePath.GetDirectory(ProgramConstants.ClientUserFilesPath, "GameCrashLogs"), "EXCEPT*.txt");
        MigrateLogFiles(SafePath.GetDirectory(ProgramConstants.ClientUserFilesPath, "SyncErrorLogs"), "SYNC*.txt");
    }

    private static void MigrateLogFiles(DirectoryInfo newDirectory, string searchPattern)
    {
        DirectoryInfo currentDirectory = SafePath.GetDirectory(ProgramConstants.ClientUserFilesPath, "ErrorLogs");
        try
        {
            if (!currentDirectory.Exists)
                return;

            if (!newDirectory.Exists)
                newDirectory.Create();

            foreach (FileInfo file in currentDirectory.EnumerateFiles(searchPattern))
            {
                string filenameTs = Path.GetFileNameWithoutExtension(file.Name);
                string[] ts = filenameTs.Split(['_'], StringSplitOptions.RemoveEmptyEntries);

                string timestamp = string.Empty;
                string baseFilename = Path.GetFileNameWithoutExtension(ts[0]);

                if (ts.Length >= 6)
                {
                    timestamp = string.Format(CultureInfo.InvariantCulture, "_{0}_{1}_{2}_{3}_{4}",
                        ts[3], ts[2].PadLeft(2, '0'), ts[1].PadLeft(2, '0'), ts[4].PadLeft(2, '0'), ts[5].PadLeft(2, '0'));
                }

                string newFilename = SafePath.CombineFilePath(newDirectory.FullName, baseFilename, timestamp, file.Extension);
                file.MoveTo(newFilename);
            }

            if (!currentDirectory.EnumerateFiles().Any())
                currentDirectory.Delete();
        }
        catch (Exception ex)
        {
            Logger.Log($"MigrateLogFiles: error moving logs from {currentDirectory.Name} to {newDirectory.Name}: {ex.Message}");
        }
    }

    private static void PruneFiles(DirectoryInfo directory, DateTime pruneThresholdTime)
    {
        if (!directory.Exists)
            return;

        try
        {
            foreach (FileSystemInfo fsEntry in directory.EnumerateFileSystemInfos())
            {
                if ((fsEntry.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    PruneFiles(new DirectoryInfo(fsEntry.FullName), pruneThresholdTime);
                else
                {
                    try
                    {
                        var fileInfo = new FileInfo(fsEntry.FullName);
                        if (fileInfo.CreationTime <= pruneThresholdTime)
                            fileInfo.Delete();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"PruneFiles: could not delete {fsEntry.Name}: {ex.Message}");
                    }
                }
            }

            if (!directory.EnumerateFileSystemInfos().Any())
                directory.Delete();
        }
        catch (Exception ex)
        {
            Logger.Log($"PruneFiles: error pruning {directory.Name}: {ex.Message}");
        }
    }
}
