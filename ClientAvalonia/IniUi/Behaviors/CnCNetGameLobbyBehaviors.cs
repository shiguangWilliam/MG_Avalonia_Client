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
    }
}
