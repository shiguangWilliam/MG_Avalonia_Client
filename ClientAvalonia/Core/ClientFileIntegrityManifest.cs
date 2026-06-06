using Rampastring.Tools;

namespace ClientAvalonia.Core;

public enum IntegrityDirectoryScanMode
{
    Flat,
    Recursive,
}

public sealed class ClientFileIntegrityManifest
{
    private const string SettingsSection = "Settings";
    private const string FilesSection = "Files";
    private const string DirectoriesSection = "Directories";

    public bool Enabled { get; private init; } = true;

    public string ManifestPath { get; private init; } = string.Empty;

    public IReadOnlyList<ClientFileAssetVerify> FileEntries { get; private init; } = [];

    public IReadOnlyDictionary<string, IntegrityDirectoryScanMode> DirectoryEntries { get; private init; }
        = new Dictionary<string, IntegrityDirectoryScanMode>();

    public static ClientFileIntegrityManifest? TryLoad(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return null;

        var ini = new IniFile(manifestPath);
        bool enabled = ini.GetBooleanValue(SettingsSection, "Enabled", true);
        if (!enabled)
        {
            return new ClientFileIntegrityManifest
            {
                Enabled = false,
                ManifestPath = manifestPath,
            };
        }

        var fileEntries = new List<ClientFileAssetVerify>();
        IniSection? filesSection = ini.GetSection(FilesSection);
        if (filesSection != null)
        {
            foreach ((string key, string value) in filesSection.Keys)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                fileEntries.Add(new ClientFileAssetVerify(NormalizeRelativePath(key), value));
            }
        }

        var directoryEntries = new Dictionary<string, IntegrityDirectoryScanMode>(StringComparer.OrdinalIgnoreCase);
        IniSection? directoriesSection = ini.GetSection(DirectoriesSection);
        if (directoriesSection != null)
        {
            foreach ((string key, string value) in directoriesSection.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                directoryEntries[NormalizeRelativePath(key)] = ParseDirectoryMode(value);
            }
        }

        return new ClientFileIntegrityManifest
        {
            Enabled = true,
            ManifestPath = manifestPath,
            FileEntries = fileEntries,
            DirectoryEntries = directoryEntries,
        };
    }

    private static IntegrityDirectoryScanMode ParseDirectoryMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "*")
            return IntegrityDirectoryScanMode.Recursive;

        return value.Trim().Equals("flat", StringComparison.OrdinalIgnoreCase)
            ? IntegrityDirectoryScanMode.Flat
            : IntegrityDirectoryScanMode.Recursive;
    }

    private static string NormalizeRelativePath(string path)
        => path.Trim().Replace('\\', '/').TrimStart('/');
}
