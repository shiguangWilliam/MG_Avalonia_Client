using ClientCore;
using ClientCore.Extensions;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>Multiplayer lobby colors from GameOptions.ini [MPColors] (XNA MultiplayerColor.LoadColors).</summary>
public static class MultiplayerColorCatalog
{
    private static IReadOnlyList<MultiplayerColorEntry>? _cache;

    public static IReadOnlyList<MultiplayerColorEntry> Load()
    {
        if (_cache != null)
            return _cache;

        string path = SafePath.CombineFilePath(AppState.Environment.BaseResourcesPath, ClientConfiguration.GAME_OPTIONS);
        var ini = new IniFile(path);
        List<string>? keys = ini.GetSectionKeys("MPColors");
        if (keys == null || keys.Count == 0)
        {
            _cache = [];
            return _cache;
        }

        var colors = new List<MultiplayerColorEntry>();
        foreach (string key in keys)
        {
            string[] values = ini.GetStringListValue("MPColors", key, "255,255,255,0");
            if (values.Length < 4)
                continue;

            if (!int.TryParse(values[0], out int r)
                || !int.TryParse(values[1], out int g)
                || !int.TryParse(values[2], out int b)
                || !int.TryParse(values[3], out int gameIndex))
                continue;

            colors.Add(new MultiplayerColorEntry
            {
                Name = key.L10N($"INI:Colors:{key}"),
                GameColorIndex = gameIndex,
                R = (byte)Math.Clamp(r, 0, 255),
                G = (byte)Math.Clamp(g, 0, 255),
                B = (byte)Math.Clamp(b, 0, 255),
            });
        }

        _cache = colors;
        return _cache;
    }

    /// <summary>RandomColor from GameOptions.ini [General] (XNA GameLobbyBase color dropdown index 0).</summary>
    public static (byte R, byte G, byte B) LoadRandomColorRgb()
    {
        string path = SafePath.CombineFilePath(AppState.Environment.BaseResourcesPath, ClientConfiguration.GAME_OPTIONS);
        var ini = new IniFile(path);
        string[] values = ini.GetStringListValue("General", "RandomColor", "255,255,255");
        if (values.Length < 3)
            return (255, 255, 255);

        return (
            (byte)Math.Clamp(int.TryParse(values[0], out int r) ? r : 255, 0, 255),
            (byte)Math.Clamp(int.TryParse(values[1], out int g) ? g : 255, 0, 255),
            (byte)Math.Clamp(int.TryParse(values[2], out int b) ? b : 255, 0, 255));
    }

    public sealed class MultiplayerColorEntry
    {
        public required string Name { get; init; }

        public int GameColorIndex { get; init; }

        public byte R { get; init; }

        public byte G { get; init; }

        public byte B { get; init; }
    }
}
