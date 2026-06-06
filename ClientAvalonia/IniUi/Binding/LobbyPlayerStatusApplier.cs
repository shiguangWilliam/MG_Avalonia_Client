using Avalonia.Media.Imaging;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>Ready / ping slot indicators (XNA XNAPlayerSlotIndicator + ping on name row).</summary>
public static class LobbyPlayerStatusApplier
{
    private const int IndicatorSize = 14;
    private const int PingIndicatorSize = 12;

    public static void Apply(
        UiNodeViewModel root,
        LobbyPlayerState playerState,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        IReadOnlyList<CnCNetGameRoomPlayer>? roomPlayers,
        bool locked,
        bool isHostView)
    {
        if (playerState.Mode != LobbyPlayerMode.Multiplayer || roomPlayers == null)
            return;

        UiNodeViewModel? panel = FindVm(root, "PlayerOptionsPanel");
        if (panel == null)
            return;

        int statusX = ReadInt(root, "PlayerStatusIndicatorX", 3);
        int statusY = ReadInt(root, "PlayerStatusIndicatorY", 1);

        for (int slot = 0; slot < LobbyPlayerSlot.MaxSlots; slot++)
        {
            UiNodeViewModel? ddName = FindVm(panel, $"ddPlayerName{slot}");
            if (ddName == null)
                continue;

            UiNodeViewModel indicator = EnsureIndicator(
                panel, slot, ddName, statusX, statusY, resources, behaviors, isPing: false);
            ApplyIndicator(indicator, slot, playerState, roomPlayers, resources, locked, isHostView);

            UiNodeViewModel pingIndicator = EnsureIndicator(
                panel, slot, ddName, statusX, statusY, resources, behaviors, isPing: true);
            ApplyPingIndicator(pingIndicator, slot, playerState, roomPlayers, resources);
        }
    }

    private static void ApplyIndicator(
        UiNodeViewModel indicator,
        int slot,
        LobbyPlayerState playerState,
        IReadOnlyList<CnCNetGameRoomPlayer> roomPlayers,
        ResourceResolver resources,
        bool locked,
        bool isHostView)
    {
        LobbyPlayerRowKind rowKind = LobbyPlayerSlotUiRules.GetUiRowKind(slot, playerState);
        if (rowKind is LobbyPlayerRowKind.Closed or LobbyPlayerRowKind.Open)
        {
            SetTexture(indicator, resources, "statusEmpty.png");
            return;
        }

        LobbyPlayerSlot slotState = playerState.Slots[slot];
        if (!slotState.IsOccupied)
        {
            SetTexture(indicator, resources, "statusEmpty.png");
            return;
        }

        if (slotState.IsAi)
        {
            SetTexture(indicator, resources, "statusAI.png");
            return;
        }

        CnCNetGameRoomPlayer? roomPlayer = roomPlayers.FirstOrDefault(
            p => p.Name.Equals(slotState.Name, StringComparison.OrdinalIgnoreCase));

        if (roomPlayer == null)
        {
            SetTexture(indicator, resources, "statusEmpty.png");
            return;
        }

        if (slot == 0 && isHostView)
        {
            SetTexture(indicator, resources, locked ? "statusOk.png" : "statusClear.png");
            return;
        }

        SetTexture(indicator, resources, roomPlayer.Ready ? "statusOk.png" : "statusClear.png");
    }

    private static void ApplyPingIndicator(
        UiNodeViewModel indicator,
        int slot,
        LobbyPlayerState playerState,
        IReadOnlyList<CnCNetGameRoomPlayer> roomPlayers,
        ResourceResolver resources)
    {
        LobbyPlayerSlot slotState = playerState.Slots[slot];
        if (!slotState.IsOccupied || slotState.IsAi)
        {
            indicator.IsVisible = false;
            return;
        }

        CnCNetGameRoomPlayer? roomPlayer = roomPlayers.FirstOrDefault(
            p => p.Name.Equals(slotState.Name, StringComparison.OrdinalIgnoreCase));

        if (roomPlayer == null || roomPlayer.Ping < 0)
        {
            indicator.IsVisible = false;
            return;
        }

        string texture = roomPlayer.Ping switch
        {
            > 350 => "ping4.png",
            > 250 => "ping3.png",
            > 100 => "ping2.png",
            _ => "ping1.png",
        };

        indicator.IsVisible = true;
        SetTexture(indicator, resources, texture);
    }

    private static UiNodeViewModel EnsureIndicator(
        UiNodeViewModel panel,
        int slot,
        UiNodeViewModel ddName,
        int statusX,
        int statusY,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        bool isPing)
    {
        string id = isPing ? $"playerPingIndicator{slot}" : $"playerStatusIndicator{slot}";
        int size = isPing ? PingIndicatorSize : IndicatorSize;

        // XNA: absolute X within PlayerOptionsPanel, Y aligned to row dropdown.
        double x = isPing ? statusX + IndicatorSize + 2 : statusX;
        double y = ddName.CanvasTop + statusY + (isPing ? 1 : 0);

        UiNodeViewModel? existing = FindVm(panel, id);
        if (existing != null)
        {
            existing.SetCanvasPosition(x, y);
            existing.Node.Props["Width"] = (double)size;
            existing.Node.Props["Height"] = (double)size;
            return existing;
        }

        var node = new UiNode
        {
            Id = id,
            ControlType = "XNAIndicator",
            TemplateKey = "DxIndicator",
        };
        node.Props["CanvasLeft"] = x;
        node.Props["CanvasTop"] = y;
        node.Props["Width"] = (double)size;
        node.Props["Height"] = (double)size;
        node.Props["IsVisible"] = !isPing;

        var vm = new UiNodeViewModel(node, resources, behaviors);
        panel.Children.Add(vm);
        return vm;
    }

    private static void SetTexture(UiNodeViewModel indicator, ResourceResolver resources, string fileName)
    {
        indicator.SetPreviewImage(resources.LoadFirstBitmap([fileName]));
    }

    private static int ReadInt(UiNodeViewModel root, string key, int fallback)
    {
        string? raw = root.GetIniString(key);
        return int.TryParse(raw, out int value) ? value : fallback;
    }

    private static UiNodeViewModel? FindVm(UiNodeViewModel root, string id)
    {
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = FindVm(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
