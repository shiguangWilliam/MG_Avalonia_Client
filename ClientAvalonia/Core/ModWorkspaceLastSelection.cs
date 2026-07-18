using System.IO;
using System.Text.Json;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>Remembers last successful ModName + InstallPath for picker highlight (not silent auto-bind).</summary>
public static class ModWorkspaceLastSelection
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public sealed record Snapshot(string ModName, string InstallPath);

    public static string StorePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClientAvalonia",
            "last-workspace.json");

    public static Snapshot? TryLoad()
    {
        try
        {
            string path = StorePath;
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            Snapshot? snap = JsonSerializer.Deserialize<Snapshot>(json, JsonOptions);
            if (snap == null || string.IsNullOrWhiteSpace(snap.ModName) || string.IsNullOrWhiteSpace(snap.InstallPath))
                return null;

            return snap with
            {
                ModName = snap.ModName.Trim(),
                InstallPath = snap.InstallPath.TrimEnd('\\', '/'),
            };
        }
        catch (Exception ex)
        {
            Logger.Log($"ModWorkspaceLastSelection: load failed: {ex.Message}");
            return null;
        }
    }

    public static void Save(string modName, string installPath)
    {
        try
        {
            string dir = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(dir);
            var snap = new Snapshot(modName.Trim(), installPath.TrimEnd('\\', '/'));
            File.WriteAllText(StorePath, JsonSerializer.Serialize(snap, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Log($"ModWorkspaceLastSelection: save failed: {ex.Message}");
        }
    }
}
