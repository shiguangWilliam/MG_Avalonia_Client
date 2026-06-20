using ClientAvalonia.CnCNet;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientCore;
using System.Linq;

namespace ClientAvalonia.IniUi.Overlays;

using ClientAvalonia.Domain.Multiplayer.CnCNet;
/// <summary>INI-driven create-game overlay (when GameCreationWindow.ini defines controls).</summary>
public static class GameCreationIniOverlayBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host, UiNodeViewModel root)
    {
        registry.Register("btnCreateGame", _ => TryCreate(host, root));
        registry.Register("btnNewGame", _ => TryCreate(host, root));
        registry.Register("btnCancel", _ => host.CloseGameCreationOverlay());
    }

    private static void TryCreate(IUiNavigationHost host, UiNodeViewModel root)
    {
        if (CnCNetSessionService.Instance.IsGameRoomJoinPending)
        {
            host.ShowStatus("Joining game room â€?please wait...");
            return;
        }

        if (CnCNetSessionService.Instance.ActiveGameRoom != null)
        {
            host.ShowStatus("Already in a game room.");
            host.CloseGameCreationOverlay();
            host.NavigateTo("CnCNetGameLobby");
            return;
        }

        string roomName = ReadText(root, "tbGameName", "tbRoomName") ?? $"{ProgramConstants.PLAYERNAME}'s Game";
        string password = (ReadText(root, "tbPassword") ?? string.Empty).Trim();
        int maxPlayers = ReadComboInt(root, "ddMaxPlayers", 8);
        int skillLevel = ReadComboIndex(root, "ddSkillLevel");

        if (CnCNetSessionService.Instance.Tunnels.Count == 0)
        {
            host.ShowStatus("No NAT tunnels available.");
            return;
        }

        CnCNetTunnel tunnel = CnCNetSessionService.Instance.Tunnels.FirstOrDefault(t => t.Official)
            ?? CnCNetSessionService.Instance.Tunnels[0];

        var request = new CnCNetGameCreationRequest
        {
            RoomName = roomName.Trim(),
            MaxPlayers = maxPlayers,
            Password = password,
            Tunnel = tunnel,
            SkillLevel = skillLevel,
        };

        host.CloseGameCreationOverlay();

        if (!CnCNetSessionService.Instance.TryCreateGame(request, out string message))
        {
            host.ShowStatus(message);
            return;
        }

        host.EnterCnCNetGameLobbyConnecting();
        host.ShowStatus(message);
    }

    private static string? ReadText(UiNodeViewModel root, params string[] ids)
    {
        foreach (string id in ids)
        {
            UiNodeViewModel? vm = FindVm(root, id);
            if (vm != null && !string.IsNullOrWhiteSpace(vm.InputText))
                return vm.InputText;
        }

        return null;
    }

    private static int ReadComboInt(UiNodeViewModel root, string id, int fallback)
    {
        UiNodeViewModel? vm = FindVm(root, id);
        if (vm == null || vm.SelectedIndex < 0 || vm.SelectedIndex >= vm.ComboItems.Count)
            return fallback;

        return int.TryParse(vm.ComboItems[vm.SelectedIndex], out int value) ? value : fallback;
    }

    private static int ReadComboIndex(UiNodeViewModel root, string id)
    {
        UiNodeViewModel? vm = FindVm(root, id);
        return vm?.SelectedIndex >= 0 ? vm.SelectedIndex : 0;
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
