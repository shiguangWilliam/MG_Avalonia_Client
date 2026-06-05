using ClientAvalonia.CnCNet;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>CnCNet in-game lobby controls (launch, leave, lock, tunnel).</summary>
public static class CnCNetGameLobbyBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host)
    {
        registry.Register("btnLockGame", _ =>
        {
            bool locked = CnCNetSessionService.Instance.GameRoom?.Locked == true;
            CnCNetSessionService.Instance.SetGameRoomLocked(!locked);
            host.ShowStatus(locked ? "Game unlocked." : "Game locked.");
        });

        registry.Register("btnChangeTunnel", _ =>
            host.ShowStatus("Tunnel selection UI pending — using default tunnel from create/join."));

        registry.Register("btnGameLobbySettings", _ =>
            host.ShowStatus("Game lobby settings UI pending."));

        registry.Register("chkAutoReady", _ =>
        {
            if (CnCNetSessionService.Instance.ActiveGameRoom?.IsHost == true)
                return;

            bool autoReady = _.IsChecked;
            if (autoReady)
            {
                CnCNetSessionService.Instance.SetGameRoomReady(true, autoReady: true);
                host.ShowStatus("Auto ready enabled.");
            }
            else
            {
                CnCNetSessionService.Instance.SetGameRoomReady(false, autoReady: false);
                host.ShowStatus("Auto ready disabled.");
            }
        });
    }
}
