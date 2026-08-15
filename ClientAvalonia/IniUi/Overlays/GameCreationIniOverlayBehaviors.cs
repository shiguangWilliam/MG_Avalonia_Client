using ClientAvalonia.CnCNet;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientCore;
using System.Linq;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.IniUi.Overlays;

using ClientAvalonia.Domain.Multiplayer.CnCNet;
/// <summary>INI-driven create-game overlay (when GameCreationWindow.ini defines controls).</summary>
public static class GameCreationIniOverlayBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host, UiNodeViewModel root)
    {
        registry.Register("btnCreateGame", _ => TryCreate(host, root));
        registry.Register("btnNewGame", _ => TryCreate(host, root));
        registry.Register("btnLoadMPGame", _ => TryCreateLoaded(host, root));
        registry.Register("btnCancel", _ => host.CloseGameCreationOverlay());
    }

    private static void TryCreateLoaded(IUiNavigationHost host, UiNodeViewModel root)
    {
        ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
        if (cncnet.IsGameRoomJoinPending || cncnet.ActiveGameRoom != null)
        {
            host.ShowStatus("Already in a game room.");
            return;
        }

        if (!MultiplayerLoadGameSupport.AllowHostingLoadedGame())
        {
            host.ShowStatus("Cannot load MP game — spawnSG.ini missing or you were not the host.");
            return;
        }

        if (cncnet.Tunnels.Count == 0)
        {
            host.ShowStatus("No NAT tunnels available.");
            return;
        }

        string roomName = ReadText(root, "tbGameName", "tbRoomName") ?? $"{AppState.Environment.PlayerName}'s Game";
        int skillLevel = ReadComboIndex(root, "ddSkillLevel");
        int maxPlayers = MultiplayerLoadGameSupport.ReadPlayerCount();
        if (maxPlayers <= 0)
            maxPlayers = 2;

        CnCNetTunnel tunnel = cncnet.TunnelSorter.TryPeekBest()
            ?? cncnet.Tunnels.FirstOrDefault(t => t.Official)
            ?? cncnet.Tunnels[0];

        string password = MultiplayerLoadGameSupport.ComputeLoadedGamePassword();
        var request = new CnCNetGameCreationRequest
        {
            RoomName = roomName.Trim(),
            MaxPlayers = maxPlayers,
            RequiresPassword = true,
            Password = password,
            Tunnel = tunnel,
            SkillLevel = skillLevel,
            IsLoadedGame = true,
        };

        host.CloseGameCreationOverlay();

        if (!cncnet.TryCreateGame(request, out string message))
        {
            host.ShowStatus(message);
            return;
        }

        host.EnterCnCNetGameLobbyConnecting();
        host.NavigateTo("CnCNetGameLoadingLobby");
        host.ShowStatus(message);
    }

    private static void TryCreate(IUiNavigationHost host, UiNodeViewModel root)
    {
        ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
        if (cncnet.IsGameRoomJoinPending)
        {
            host.ShowStatus("Joining game room - please wait...");
            return;
        }

        if (cncnet.ActiveGameRoom != null)
        {
            host.ShowStatus("Already in a game room.");
            host.CloseGameCreationOverlay();
            host.NavigateTo("CnCNetGameLobby");
            return;
        }

        string roomName = ReadText(root, "tbGameName", "tbRoomName") ?? $"{AppState.Environment.PlayerName}'s Game";
        bool requiresPassword = ReadCheckBox(root, "chkRequiresPassword", "chkPasswordProtect");
        string password = (ReadText(root, "tbPassword") ?? string.Empty).Trim();
        if (!requiresPassword && !string.IsNullOrWhiteSpace(password))
            requiresPassword = true;
        if (requiresPassword && string.IsNullOrWhiteSpace(password))
        {
            host.ShowStatus("Enter a password or disable password protection.");
            return;
        }
        int maxPlayers = ReadComboInt(root, "ddMaxPlayers", 8);
        int skillLevel = ReadComboIndex(root, "ddSkillLevel");

        if (cncnet.Tunnels.Count == 0)
        {
            host.ShowStatus("No NAT tunnels available.");
            return;
        }

        // Prefer TunnelSorter min-heap best (lowest ping); Official only as cold-start fallback.
        CnCNetTunnel tunnel = cncnet.TunnelSorter.TryPeekBest()
            ?? cncnet.Tunnels.FirstOrDefault(t => t.Official)
            ?? cncnet.Tunnels[0];

        var request = new CnCNetGameCreationRequest
        {
            RoomName = roomName.Trim(),
            MaxPlayers = maxPlayers,
            RequiresPassword = requiresPassword,
            Password = requiresPassword ? password : string.Empty,
            Tunnel = tunnel,
            SkillLevel = skillLevel,
        };

        host.CloseGameCreationOverlay();

        if (!cncnet.TryCreateGame(request, out string message))
        {
            host.ShowStatus(message);
            return;
        }

        host.EnterCnCNetGameLobbyConnecting();
        host.ShowStatus(message);
    }

    private static bool ReadCheckBox(UiNodeViewModel root, params string[] ids)
    {
        foreach (string id in ids)
        {
            UiNodeViewModel? vm = FindVm(root, id);
            if (vm != null)
                return vm.IsChecked;
        }

        return false;
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
