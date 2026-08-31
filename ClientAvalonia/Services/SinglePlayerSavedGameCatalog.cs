using ClientAvalonia.GlobalState;
using ClientCore;
using OpenMcdf;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Single-player <c>*.SAV</c> entry (DX <c>DTAClient.Domain.SavedGame</c>).</summary>
public sealed class SinglePlayerSavedGame
{
    public required string FileName { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public DateTime LastModified { get; init; }
}

/// <summary>Scans <c>Saved Games/*.SAV</c> and parses OLE Scenario Description via OpenMcdf.</summary>
public static class SinglePlayerSavedGameCatalog
{
    private const string DirectoryName = "Saved Games";

    public static IReadOnlyList<SinglePlayerSavedGame> ListSaves(string? gamePath = null)
    {
        string root = gamePath ?? AppState.Environment.GamePath;
        DirectoryInfo dir = SafePath.GetDirectory(root, DirectoryName);
        if (!dir.Exists)
            return [];

        var list = new List<SinglePlayerSavedGame>();
        foreach (FileInfo file in dir.EnumerateFiles("*.SAV", SearchOption.TopDirectoryOnly))
        {
            if (TryParse(file, out SinglePlayerSavedGame? sg) && sg != null)
                list.Add(sg);
        }

        return list.OrderByDescending(s => s.LastModified).ToArray();
    }

    public static bool TryDelete(string fileName, string? gamePath = null)
    {
        string root = gamePath ?? AppState.Environment.GamePath;
        FileInfo file = SafePath.GetFile(root, DirectoryName, fileName);
        if (!file.Exists)
            return false;
        file.Delete();
        return true;
    }

    private static bool TryParse(FileInfo file, out SinglePlayerSavedGame? saved)
    {
        saved = null;
        try
        {
            string display;
            using (Stream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var cf = new CompoundFile(stream);
                byte[] bytes = cf.RootStorage.GetStream("Scenario Description").GetData();
                display = System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                cf.Close();
            }

            saved = new SinglePlayerSavedGame
            {
                FileName = file.Name,
                DisplayName = string.IsNullOrWhiteSpace(display) ? file.Name : display,
                LastModified = file.LastWriteTime,
            };
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to parse saved game {file.Name}: {ex.Message}");
            return false;
        }
    }
}
