using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Initializes Rampastring file logging under Client/client.log (aligned with DXMainClient PreStartup).</summary>
public static class ClientLogService
{
    private static bool _initialized;

    public static bool IsInitialized => _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;

        try
        {
            Directory.CreateDirectory(ProgramConstants.ClientUserFilesPath);

            string logPath = SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, "client.log");
            string previousPath = SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, "client_previous.log");

            if (File.Exists(logPath))
            {
                if (File.Exists(previousPath))
                    File.Delete(previousPath);
                File.Move(logPath, previousPath);
            }

            Logger.Initialize(ProgramConstants.ClientUserFilesPath, "client.log");
            Logger.WriteLogFile = true;
            ProgramConstants.LogFileName = logPath;

            Logger.Log("*** ClientAvalonia logfile ***");
            Logger.Log($"Game root: {Environment.CurrentDirectory}");
            Logger.Log($"LocalGame: {ClientConfiguration.Instance.LocalGame}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ClientLogService initialization failed: {ex.Message}");
        }
    }
}
