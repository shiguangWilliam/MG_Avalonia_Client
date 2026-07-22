using ClientAvalonia.CnCNet;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.Services;
using ClientAvalonia.Session;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>CnCNet in-game lobby controls (launch, leave, lock, tunnel).</summary>
public static class CnCNetGameLobbyBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host)
    {
        registry.Register("btnLockGame", _ =>
        {
            ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
            bool locked = cncnet.GameRoom?.Locked == true;
            cncnet.SetGameRoomLocked(!locked);
            host.ShowStatus(locked ? "Game unlocked." : "Game locked.");
            host.RefreshCnCNetGameRoomPlayers();
        });

        registry.Register("btnChangeTunnel", _ => host.OpenGameRoomTunnelSelection());

        registry.Register("btnGameLobbySettings", _ => host.OpenGameLobbySettingsOverlay());

        registry.Register("chkAutoReady", _ =>
        {
            ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
            if (cncnet.ActiveGameRoom?.IsHost == true)
                return;

            bool autoReady = _.IsChecked;
            if (autoReady)
            {
                cncnet.SetGameRoomReady(true, autoReady: true);
                host.ShowStatus("Auto ready enabled.");
            }
            else
            {
                cncnet.SetGameRoomReady(false, autoReady: false);
                host.ShowStatus("Auto ready disabled.");
            }

            host.RefreshCnCNetGameRoomPlayers();
        });

        registry.Register("btnManualReady", _ => ToggleJoinerReady(host));
    }

    private static void ToggleJoinerReady(IUiNavigationHost host)
    {
        ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
        ICnCNetGameSession? room = cncnet.ActiveGameRoom;
        if (room == null || room.IsHost)
            return;

        CnCNetGameRoomPlayer? local = cncnet.GameRoom?.Players
            .FirstOrDefault(p => p.Name.Equals(cncnet.LocalNick, StringComparison.OrdinalIgnoreCase));

        bool ready = !(local?.Ready ?? false);
        cncnet.SetGameRoomReady(ready, autoReady: false);
        host.ShowStatus(ready ? "Ready — waiting for host to launch." : "Not ready.");
        host.RefreshCnCNetGameRoomPlayers();
    }
}
