using ClientAvalonia.Domain;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Skirmish / in-game lobby player slots (8 rows), aligned with GameLobbyBase.</summary>
public sealed class LobbyPlayerState
{
    public const string SkirmishSettingsRelativePath = "Client/SkirmishSettings.ini";

    public LobbyPlayerSlot[] Slots { get; } = Enumerable.Range(0, LobbyPlayerSlot.MaxSlots)
        .Select(_ => new LobbyPlayerSlot())
        .ToArray();

    public IReadOnlyList<string> SideNames { get; private set; } = [];

    public IReadOnlyList<LobbySideEntry> SideEntries { get; private set; } = [];

    public IReadOnlyList<string> AiNames { get; private set; } = [];

    public IReadOnlyList<string> TeamNames { get; private set; } = [];

    public void LoadDefaults(bool includeSpectator = true)
    {
        SideEntries = LobbySideCatalog.Load(includeSpectator);
        SideNames = SideEntries.Select(s => s.DisplayName).ToList();
        AiNames = ProgramConstants.AI_PLAYER_NAMES.ToList();
        TeamNames = ProgramConstants.TEAMS.ToList();

        ClearSlots();
        Slots[0].Name = ProgramConstants.PLAYERNAME;
        Slots[0].IsHumanLocal = true;
        Slots[0].SideIndex = 0;
        Slots[0].ColorIndex = 0;
        Slots[0].TeamIndex = 0;
        Slots[0].StartIndex = 0;

        Slots[1].Name = AiNames[0];
        Slots[1].IsAi = true;
        Slots[1].AiLevel = 0;
        Slots[1].SideIndex = 0;
        Slots[1].ColorIndex = 0;
        Slots[1].TeamIndex = 0;
        Slots[1].StartIndex = 0;
    }

    public void ClearSlots()
    {
        foreach (LobbyPlayerSlot slot in Slots)
        {
            slot.Name = string.Empty;
            slot.IsAi = false;
            slot.IsHumanLocal = false;
            slot.SideIndex = 0;
            slot.ColorIndex = 0;
            slot.StartIndex = 0;
            slot.TeamIndex = 0;
            slot.AiLevel = 0;
        }
    }

    public int HumanCount => Slots.Count(s => s.IsOccupied && !s.IsAi);

    public int AiCount => Slots.Count(s => s.IsOccupied && s.IsAi);

    public bool TryLoadSkirmishSettings()
    {
        string path = SafePath.CombineFilePath(ProgramConstants.GamePath, SkirmishSettingsRelativePath);
        if (!File.Exists(path))
            return false;

        var ini = new IniFile(path);
        ClearSlots();

        string humanRaw = ini.GetStringValue("Player", "Info", string.Empty);
        if (!TryParsePlayerLine(humanRaw, out LobbyPlayerSlot? human) || human == null)
            return false;

        human.Name = ProgramConstants.PLAYERNAME;
        human.IsHumanLocal = true;
        Slots[0] = human;

        List<string>? aiKeys = ini.GetSectionKeys("AIPlayers");
        if (aiKeys == null)
            return true;

        int aiSlot = 1;
        foreach (string key in aiKeys.OrderBy(k => int.TryParse(k, out int i) ? i : int.MaxValue))
        {
            if (aiSlot >= LobbyPlayerSlot.MaxSlots)
                break;

            string raw = ini.GetStringValue("AIPlayers", key, string.Empty);
            if (TryParsePlayerLine(raw, out LobbyPlayerSlot? ai) && ai != null)
            {
                ai.IsAi = true;
                Slots[aiSlot] = ai;
                aiSlot++;
            }
        }

        return true;
    }

    public void SaveSkirmishSettings()
    {
        string path = SafePath.CombineFilePath(ProgramConstants.GamePath, SkirmishSettingsRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var ini = new IniFile(path);
        LobbyPlayerSlot? human = Slots.FirstOrDefault(s => s.IsOccupied && !s.IsAi);
        if (human != null)
            ini.SetStringValue("Player", "Info", FormatPlayerLine(human, 0));

        int aiIndex = 0;
        foreach (LobbyPlayerSlot slot in Slots.Where(s => s.IsOccupied && s.IsAi))
        {
            ini.SetStringValue("AIPlayers", aiIndex.ToString(), FormatPlayerLine(slot, aiIndex + 1));
            aiIndex++;
        }

        ini.WriteIniFile();
    }

    public static bool TryParsePlayerLine(string raw, out LobbyPlayerSlot? slot)
    {
        slot = null;
        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 7)
            return false;

        slot = new LobbyPlayerSlot
        {
            Name = parts[0],
            SideIndex = int.TryParse(parts[1], out int side) ? side : 0,
            StartIndex = int.TryParse(parts[2], out int start) ? start : 0,
            ColorIndex = int.TryParse(parts[3], out int color) ? color : 0,
            TeamIndex = int.TryParse(parts[4], out int team) ? team : 0,
            AiLevel = int.TryParse(parts[5], out int ai) ? ai : 0,
            IsAi = bool.TryParse(parts[6], out bool isAi) && isAi,
        };
        return !string.IsNullOrWhiteSpace(slot.Name);
    }

    public static string FormatPlayerLine(LobbyPlayerSlot slot, int index)
        => string.Join(',', slot.Name, slot.SideIndex, slot.StartIndex, slot.ColorIndex, slot.TeamIndex, slot.AiLevel, slot.IsAi, index);

    public static int HouseHandicapFromAiLevel(int aiLevel) => Math.Abs(aiLevel - 2);
}
