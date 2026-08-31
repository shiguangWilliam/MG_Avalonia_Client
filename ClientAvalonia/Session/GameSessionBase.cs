using ClientAvalonia.Domain;

namespace ClientAvalonia.Session;

/// <summary>
/// 三会话（Skirmish / LAN / CnCNet）共享的槽位核心基类（架构设计稿 D2）。
///
/// 设计约定（见 note/architecture-issue-list-2026-08-25.md 附二）：
/// <list type="bullet">
/// <item>本类不声明实现 <see cref="IGameSession"/>——<c>Map/Options/State</c> 等会话形状
///   成员由各子类按自己的类型实现（避免属性协变/显式实现摩擦）；子类声明接口后，
///   本类的 public 成员（<see cref="PlayerSlots"/>/<see cref="SlotSink"/>/
///   <see cref="Revision"/>/<see cref="StateChanged"/>/<see cref="NotifyStateChanged"/>）
///   自然满足接口要求。</item>
/// <item>共享核心（非 virtual）：槽位数组 + <see cref="SlotSink"/> + <see cref="Revision"/> ——
///   三会话原本各复制约 40-50 行同构样板。</item>
/// <item>四个 seam（见各成员注释）：切图重置策略 / 本地改动副作用 / 网络灌入 / 通知亲和。</item>
/// <item>基类不内置锁：多线程子类（CnCNet）在自己的 <c>_sync</c> 里组合调用基类原语。</item>
/// <item>Revision 一律 <see cref="Interlocked"/>（对齐 CnCNetGameRoomSession 既有做法，
///   修正 Skirmish/LAN 此前 <c>_revision++</c> 的非原子写）。</item>
/// </list>
/// </summary>
public abstract class GameSessionBase
{
    private long _revision;

    /// <summary>共享槽位数组（元素实现 <see cref="IPlayerSlot"/> 与 CnCNet 扩展接口）。</summary>
    protected readonly LobbyPlayerSlot[] CoreSlots =
        Enumerable.Range(0, LobbyPlayerSlot.MaxSlots)
            .Select(_ => new LobbyPlayerSlot())
            .ToArray();

    protected GameSessionBase()
    {
        SlotSink = new LobbyPlayerSlotSink(() => CoreSlots, RaiseStateChanged);
    }

    /// <summary>
    /// seam 1（abstract）：切换地图时重置槽位。三会话策略不同——
    /// Skirmish 自动填充、CnCNet 房主保留人类清 AI、LAN 保留人类。
    /// </summary>
    public abstract void ResetSlotsForMap(int maxPlayers);

    /// <summary>
    /// seam 2（virtual）：本地 UI 改动槽位后的副作用（广播/持久化触发）。
    /// 基类默认无操作；CnCNet 房主覆写为 PO 广播，LAN 覆写为广播槽位。
    /// </summary>
    protected virtual void OnLocalSlotMutated()
    {
    }

    /// <summary>
    /// seam 3（virtual）：网络灌入槽位（入站 PO / LAN POPTS）的统一入口（D7 后续步骤接线）。
    /// 默认无操作；覆写实现<b>绝不</b>触发 <see cref="OnLocalSlotMutated"/>（反回声不变量）。
    /// </summary>
    protected virtual void ApplyExternalSlots()
    {
    }

    /// <summary>
    /// seam 4（virtual）：状态变更通知的线程亲和。基类默认在调用线程直接 Invoke；
    /// 多线程子类覆写为 marshal 到 UI 线程。
    /// </summary>
    protected virtual void RaiseStateChanged()
    {
        Interlocked.Increment(ref _revision);
        StateChanged?.Invoke();
    }

    /// <summary>会话级原子脏读 tag（防 UI 重入）。</summary>
    public long Revision => _revision;

    /// <summary>共享槽位只读视图。</summary>
    public IReadOnlyList<IPlayerSlot> PlayerSlots => CoreSlots;

    /// <summary>槽位写入收口（契约见 <see cref="IPlayerSlotSink"/>）。</summary>
    public IPlayerSlotSink SlotSink { get; }

    /// <summary>通知 UI 刷新（批量写入完成后调用一次），走 <see cref="RaiseStateChanged"/>。</summary>
    public void NotifyStateChanged() => RaiseStateChanged();

    /// <summary>
    /// 仅触发 <see cref="StateChanged"/>，不递增 <see cref="Revision"/>。
    /// 供 CnCNet 等子类中历史路径使用（地图/聊天/Ready 等与槽位 Revision 无关的刷新）。
    /// </summary>
    protected void FireStateChanged() => StateChanged?.Invoke();

    /// <summary>状态或槽位变化时触发（UI 刷新）。</summary>
    public event Action? StateChanged;
}
