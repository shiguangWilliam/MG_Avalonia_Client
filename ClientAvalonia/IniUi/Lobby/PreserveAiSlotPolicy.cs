using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.Session;

namespace ClientAvalonia.IniUi.Lobby;

/// <summary>
/// Map-switch AI slot policy that PRESERVES the player's prior adjustments:
/// keep AI rows (level/side/color/team/start) when the new map fits, truncate
/// the tail when it shrinks, append defaults when it grows.
/// Complements <see cref="DefaultAiSlotPolicy"/> (non-preserving, used on first entry).
/// </summary>
public static class PreserveAiSlotPolicy
{
    /// <summary>
    /// Resizes the AI rows of <paramref name="session"/> to the new map capacity while
    /// keeping the human slot and as many adjusted AI rows as possible.
    /// </summary>
    /// <param name="session">Skirmish session owning the slots.</param>
    /// <param name="maxPlayers">New map MaxPlayers.</param>
    /// <param name="colors">Color catalog (defaults for appended AIs).</param>
    /// <param name="aiNames">AI name list (defaults for appended AIs).</param>
    /// <param name="fillToCapacity">
    /// true（地图切换语义）：容量富余时追加默认 AI 行；
    /// false（设置恢复语义，DX SkirmishLobby.LoadSettings）：保存了多少 AI 就恢复多少，
    /// 只按地图容量裁剪越界行，不追加。
    /// </param>
    public static void ResizeToMapCapacity(
        ISkirmishSession session,
        int maxPlayers,
        IMultiplayerColorCatalog colors,
        IReadOnlyList<string>? aiNames = null,
        bool fillToCapacity = true)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (colors == null) throw new ArgumentNullException(nameof(colors));

        if (maxPlayers < 1) maxPlayers = 1;
        if (maxPlayers > LobbyPlayerSlot.MaxSlots) maxPlayers = LobbyPlayerSlot.MaxSlots;

        IPlayerSlot human = session.PlayerSlots[0];
        string playerName = !string.IsNullOrWhiteSpace(human.Name) ? human.Name : "Player";
        bool humanIsLocal = human.IsHumanLocal;
        int humanSide = human.SideIndex;
        int humanColor = human.ColorIndex;
        int humanTeam = human.TeamIndex;
        int humanStart = human.StartIndex <= maxPlayers ? human.StartIndex : 0;

        var preservedAis = new List<LobbyPlayerSlot>();
        for (int i = 1; i < session.PlayerSlots.Count; i++)
        {
            IPlayerSlot slot = session.PlayerSlots[i];
            if (slot.IsOccupied && slot.IsAi)
                preservedAis.Add(slot is LobbyPlayerSlot concrete ? concrete.Clone() : ToClone(slot));
        }

        int targetAiCount = fillToCapacity ? maxPlayers - 1 : Math.Min(preservedAis.Count, maxPlayers - 1);
        var kept = preservedAis.Take(targetAiCount).ToList();

        IReadOnlyList<string> names = aiNames ?? [];
        int colorCount = Math.Max(1, colors.Load().Count);

        List<LobbyPlayerSlot> grid = LobbySlotGrid.CreateEmpty();

        grid[0].Name = playerName;
        grid[0].IsHumanLocal = humanIsLocal;
        grid[0].IsAi = false;
        grid[0].SideIndex = humanSide;
        grid[0].ColorIndex = humanColor;
        grid[0].TeamIndex = humanTeam;
        grid[0].StartIndex = humanStart;

        int row = 1;
        foreach (LobbyPlayerSlot ai in kept)
        {
            if (row >= LobbyPlayerSlot.MaxSlots)
                break;

            LobbyPlayerSlot target = grid[row];
            target.Name = ai.Name;
            target.IsAi = true;
            target.IsHumanLocal = false;
            target.AiLevel = ai.AiLevel;
            target.SideIndex = ai.SideIndex;
            target.ColorIndex = ai.ColorIndex;
            target.TeamIndex = ai.TeamIndex;
            target.StartIndex = ai.StartIndex <= maxPlayers ? ai.StartIndex : 0;
            row++;
        }

        if (fillToCapacity)
        {
            for (int i = row; i < maxPlayers; i++)
            {
                LobbyPlayerSlot slot = grid[i];
                slot.Name = names.Count > 0 ? names[(i - 1) % names.Count] : $"AI {i}";
                slot.IsAi = true;
                slot.IsHumanLocal = false;
                slot.AiLevel = 0;
                slot.SideIndex = 0;
                slot.ColorIndex = i % colorCount;
                slot.TeamIndex = 0;
                slot.StartIndex = 0;
            }
        }

        LobbySlotGrid.ApplyToSink(session, grid);
    }

    private static LobbyPlayerSlot ToClone(IPlayerSlot slot)
        => new()
        {
            Name = slot.Name,
            SideIndex = slot.SideIndex,
            ColorIndex = slot.ColorIndex,
            TeamIndex = slot.TeamIndex,
            StartIndex = slot.StartIndex,
            AiLevel = slot.AiLevel,
            IsAi = slot.IsAi,
            IsHumanLocal = slot.IsHumanLocal,
        };
}
