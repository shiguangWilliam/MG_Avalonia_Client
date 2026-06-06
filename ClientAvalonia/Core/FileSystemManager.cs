using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>
/// Resolves install and user-data paths. Install root prefers the Windows registry
/// InstallPath value, then ProgramConstants.GamePath.
/// </summary>
public sealed class FileSystemManager
{
    public const string DefaultIntegrityManifestRelativePath = "Resources/ClientFileIntegrity.ini";

    private readonly string _installRoot;

    public FileSystemManager(string? installRoot = null)
    {
        _installRoot = NormalizeDirectory(installRoot ?? ResolveInstallRoot());
    }

    public string InstallRoot => _installRoot;

    public string GetUserDataPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CNCNetClient");

    public string GetGameInstallationPath() => _installRoot;

    public string ResolveManifestPath(string? manifestRelativePath = null)
    {
        string relative = string.IsNullOrWhiteSpace(manifestRelativePath)
            ? DefaultIntegrityManifestRelativePath
            : manifestRelativePath.Trim();

        return SafePath.CombineFilePath(_installRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public string ResolveRelativePath(string relativePath)
        => SafePath.CombineFilePath(_installRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string ResolveInstallRoot()
    {
        string? fromRegistry = InstallationRegistry.TryReadInstallPath();
        if (!string.IsNullOrWhiteSpace(fromRegistry) && Directory.Exists(fromRegistry))
            return fromRegistry;

        if (ClientCoreBootstrap.IsInitialized && !string.IsNullOrWhiteSpace(ProgramConstants.GamePath))
            return ProgramConstants.GamePath;

        return Directory.GetCurrentDirectory();
    }

    public static string NormalizeDirectory(string path)
        => Path.GetFullPath(path.TrimEnd('\\', '/'));
}
