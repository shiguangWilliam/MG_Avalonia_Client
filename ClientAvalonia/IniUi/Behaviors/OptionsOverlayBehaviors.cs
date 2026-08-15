using System.Threading.Tasks;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientUpdater;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>Save/Cancel on the options floating panel (does not replace main-window navigation).</summary>
public static class OptionsOverlayBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host)
    {
        registry.Register("btnSave", _ =>
        {
            if (!host.IsFloatingOverlayOpen)
                return;

            host.CommitSettings();
            host.ShowStatus("设置已保存");
        });

        RegisterClose(registry, host, "btnOK", commit: true);
        RegisterClose(registry, host, "btnCancel", discard: true);

        registry.Register("btnMoveUp", _ =>
        {
            if (host.IsOptionsOverlayOpen && host.OverlayRoot is UiNodeViewModel root)
                UpdaterOptionsApplier.MoveSelectedMirrorUp(root);
        });
        registry.Register("btnMoveDown", _ =>
        {
            if (host.IsOptionsOverlayOpen && host.OverlayRoot is UiNodeViewModel root)
                UpdaterOptionsApplier.MoveSelectedMirrorDown(root);
        });
        registry.Register("btnForceUpdate", _ =>
        {
            if (!host.IsFloatingOverlayOpen)
                return;

            host.CommitSettings();
            Updater.ClearVersionInfo();
            host.CloseFloatingOverlay();
            host.CheckForUpdates();
            host.ShowStatus("Force update: checking for updates…");
        });

        registry.Register("btnWafBlockAdd", _ =>
        {
            if (!host.IsOptionsOverlayOpen || host.OverlayRoot is not UiNodeViewModel root)
                return;

            if (WafBlocklistApplier.TryAddFromInput(root, CnCNetSessionService.Instance.IngressWaf, out string status))
                host.ShowStatus(status);
            else
                host.ShowStatus(status);
        });

        registry.Register("btnWafStrategies", __ =>
        {
            if (!host.IsOptionsOverlayOpen)
                return;

            _ = OpenStrategiesAsync(host);
        });

        registry.Register("btnWafBlockRemove", _ =>
        {
            if (!host.IsOptionsOverlayOpen || host.OverlayRoot is not UiNodeViewModel root)
                return;

            WafBlocklistApplier.RemoveSelected(root, CnCNetSessionService.Instance.IngressWaf);
            host.ShowStatus("已从屏蔽名单移除选中项");
        });

        registry.Register("btnWafBlockClear", __ =>
        {
            if (!host.IsOptionsOverlayOpen || host.OverlayRoot is not UiNodeViewModel root)
                return;

            _ = ClearBlocklistAsync(host, root);
        });
    }

    private static async Task OpenStrategiesAsync(IUiNavigationHost host)
    {
        await ClientDialogService.ShowWafStrategiesAsync(
            owner: null,
            CnCNetSessionService.Instance.IngressWaf);
        host.ShowStatus("WAF 策略已更新");
    }

    private static async Task ClearBlocklistAsync(IUiNavigationHost host, UiNodeViewModel root)
    {
        bool ok = await ClientDialogService.ConfirmAsync(
            owner: null,
            title: "清空 WAF 屏蔽名单",
            message: "确定清空全部屏蔽/Drop 词条吗？此操作不可撤销。");
        if (!ok)
            return;

        WafBlocklistApplier.ClearAll(root, CnCNetSessionService.Instance.IngressWaf);
        host.ShowStatus("已清空 WAF 屏蔽名单");
    }

    private static void RegisterClose(
        BehaviorRegistry registry,
        IUiNavigationHost host,
        string controlId,
        bool commit = false,
        bool discard = false)
        => registry.Register(controlId, _ =>
        {
            if (!host.IsFloatingOverlayOpen)
                return;

            if (commit)
                host.CommitSettings();
            else if (discard)
                host.DiscardSettings();

            host.CloseFloatingOverlay();
            host.ShowStatus(commit ? "Settings saved" : "Settings closed");
        });
}
