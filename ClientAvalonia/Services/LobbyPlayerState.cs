using ClientAvalonia.Domain;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

public enum LobbyPlayerMode
{
    Skirmish,
    Multiplayer,
}

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

    public LobbyPlayerMode Mode { get; set; } = LobbyPlayerMode.Skirmish;

    public bool AllowHostPlayerOptions { get; set; } = true;

    public string LocalPlayerName { get; set; } = ProgramConstants.PLAYERNAME;

    public string HostPlayerName { get; set; } = ProgramConstants.PLAYERNAME;

    /// <summary>Suppresses UI→state sync while applying CopyPlayerDataToUI (XNA PlayerUpdatingInProgress).</summary>
    public bool PlayerUpdatingInProgress { get; set; }

    public void LoadCatalogs(bool includeSpectator = true)
    {
        SideEntries = LobbySideCatalog.Load(includeSpectator);
        SideNames = SideEntries.Select(s => s.DisplayName).ToList();
        AiNames = ProgramConstants.AI_PLAYER_NAMES.ToList();
        TeamNames = ProgramConstants.TEAMS.ToList();
    }

    public void LoadDefaults(bool includeSpectator = true)
    {
        LoadCatalogs(includeSpectator);
        LoadDefaultSkirmishSlots();
    }

    public void LoadDefaultSkirmishSlots()
    {
        ClearSlots();
        Slots[0].Name = ProgramConstants.PLAYERNAME;
        Slots[0].IsHumanLocal = true;
        Slots[0].SideIndex = 0;
        Slots[0].ColorIndex = 0;
        Slots[0].TeamIndex = 0;
        Slots[0].StartIndex = 0;

        if (AiNames.Count == 0)
            return;

        Slots[1].Name = AiNames[0];
        Slots[1].IsAi = true;
        Slots[1].AiLevel = 0;
        Slots[1].SideIndex = 0;
        Slots[1].ColorIndex = 0;
        Slots[1].TeamIndex = 0;
        Slots[1].StartIndex = 0;
    }

    public int FirstEmptySlotIndex()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            if (!Slots[i].IsOccupied)
                return i;
        }

        return -1;
    }

    public int OccupiedSlotCount => Slots.Count(s => s.IsOccupied);

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

    public int HumanCount => HumanRowCount;

    public int AiCount => AiRowCount;

    /// <summary>Consecutive human rows from slot 0 (XNA Players list).</summary>
    public int HumanRowCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i].IsOccupied && !Slots[i].IsAi)
                    count++;
                else
                    break;
            }

            return count;
        }
    }

    /// <summary>Consecutive AI rows after humans (XNA AIPlayers list).</summary>
    public int AiRowCount
    {
        get
        {
            int start = HumanRowCount;
            int count = 0;
            for (int i = start; i < Slots.Length; i++)
            {
                if (Slots[i].IsOccupied && Slots[i].IsAi)
                    count++;
                else
                    break;
            }

            return count;
        }
    }

    public int OccupiedRowCount => HumanRowCount + AiRowCount;

    /// <summary>Repack humans (host first) + AIs into consecutive rows (XNA Players + AIPlayers).</summary>
    public void RepopulateRows(IReadOnlyList<LobbyPlayerSlot> humans, IReadOnlyList<LobbyPlayerSlot> ais)
    {
        ClearSlots();
        int row = 0;
        foreach (LobbyPlayerSlot human in humans)
        {
            if (row >= Slots.Length)
                break;

            Slots[row] = human.Clone();
            row++;
        }

        foreach (LobbyPlayerSlot ai in ais)
        {
            if (row >= Slots.Length)
                break;

            Slots[row] = ai.Clone();
            row++;
        }
    }

    /// <summary>Host is always Players[0] in DXMain; ensure row 0 when hosting.</summary>
    public void EnsureHostAsFirstHuman(string hostName, string localNick)
    {
        hostName = NormalizeNick(hostName, localNick);

        var humans = new List<LobbyPlayerSlot>();
        var ais = new List<LobbyPlayerSlot>();
        foreach (LobbyPlayerSlot slot in Slots)
        {
            if (!slot.IsOccupied)
                continue;

            if (slot.IsAi)
                ais.Add(slot.Clone());
            else
                humans.Add(slot.Clone());
        }

        LobbyPlayerSlot host = humans.FirstOrDefault(h =>
            h.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase))
            ?? new LobbyPlayerSlot
            {
                Name = hostName,
                SideIndex = 0,
                ColorIndex = 0,
                TeamIndex = 0,
                StartIndex = 0,
            };

        host.IsAi = false;
        host.IsHumanLocal = host.Name.Equals(localNick, StringComparison.OrdinalIgnoreCase);

        humans.RemoveAll(h => h.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase));
        humans.Insert(0, host);
        RepopulateRows(humans, ais);
    }

    public void MarkLocalHuman(string localNick)
    {
        foreach (LobbyPlayerSlot slot in Slots)
        {
            if (slot.IsOccupied && !slot.IsAi)
                slot.IsHumanLocal = slot.Name.Equals(localNick, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeNick(string primary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
            return primary.Trim();

        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback.Trim();

        return ProgramConstants.PLAYERNAME;
    }

    public LobbyPlayerRowKind GetRowKind(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Length)
            return LobbyPlayerRowKind.Closed;

        int humans = HumanRowCount;
        int ais = AiRowCount;

        if (slotIndex < humans)
            return LobbyPlayerRowKind.Human;

        if (slotIndex < humans + ais)
            return LobbyPlayerRowKind.Ai;

        if (slotIndex == humans + ais)
            return LobbyPlayerRowKind.Open;

        return LobbyPlayerRowKind.Closed;
    }

    /// <summary>Rebuild AI rows from UI starting at first AI row (XNA CopyPlayerDataFromUI).</summary>
    public void RebuildAiRowsFromUi(int firstAiRow)
    {
        var preserved = new List<LobbyPlayerSlot>();
        for (int i = firstAiRow; i < Slots.Length; i++)
        {
            LobbyPlayerSlot slot = Slots[i];
            if (slot.IsOccupied && slot.IsAi)
                preserved.Add(slot.Clone());
        }

        for (int i = firstAiRow; i < Slots.Length; i++)
            Slots[i].Clear();

        int row = firstAiRow;
        foreach (LobbyPlayerSlot ai in preserved)
        {
            if (row >= Slots.Length)
                break;

            Slots[row] = ai;
            row++;
        }
    }

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
