using ClientCore;

namespace ClientAvalonia.Core;

public sealed class FileSystemManager
{
    public string GetUserDataPath()
    {
        // Implement logic to get the user data path.
        // This is a placeholder implementation and should be replaced with actual path retrieval code.
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CNCNetClient");
    }

    public string GetGameInstallationPath()
    {
        // Implement logic to get the game installation path.
        // This is a placeholder implementation and should be replaced with actual path retrieval code.
        return @"C:\Games\RedAlert2";
    }
}