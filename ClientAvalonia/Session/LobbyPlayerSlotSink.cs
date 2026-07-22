using System;

namespace ClientAvalonia.Session;

/// <summary>
/// 通用 <see cref="IPlayerSlotSink"/> 实现：操作一个外部提供的 <c>IPlayerSlot[]</c>，
/// 通过回调通知 Session 状态变更。
///
/// 设计要点：
/// <list type="bullet">
/// <item>不持有自己的数组——通过 <c>slotsAccessor</c> 委托每次访问最新数组，
/// 避免与 owning Session 的数组生命周期脱钩。</item>
/// <item>静默 vs 非静默由 <c>silent</c> 参数控制；非静默写入后调用 <c>onChanged</c>。</item>
/// <item>无锁——并发安全由 owning Session 保证（如 CnCNetGameRoomSession 内的 _sync）。</item>
/// </list>
///
/// 使用方式（在 Session 构造时）：
/// <code>
/// SlotSink = new LobbyPlayerSlotSink(() => _slots, () => StateChanged?.Invoke());
/// </code>
/// </summary>
public sealed class LobbyPlayerSlotSink : IPlayerSlotSink
{
    private readonly Func<IPlayerSlot[]> _slotsAccessor;
    private readonly Action? _onChanged;

    /// <param name="slotsAccessor">返回当前槽位数组的委托（每次调用返回最新引用）。</param>
    /// <param name="onChanged">非静默写入完成后的回调（用于触发 StateChanged）。</param>
    public LobbyPlayerSlotSink(Func<IPlayerSlot[]> slotsAccessor, Action? onChanged = null)
    {
        _slotsAccessor = slotsAccessor ?? throw new ArgumentNullException(nameof(slotsAccessor));
        _onChanged = onChanged;
    }

    /// <inheritdoc />
    public void OverwriteSlot(int index, IPlayerSlot source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        IPlayerSlot[] slots = _slotsAccessor();
        if ((uint)index >= (uint)slots.Length) return;

        OverwriteSlotSilent(index, source);
        _onChanged?.Invoke();
    }

    /// <inheritdoc />
    public void OverwriteSlotSilent(int index, IPlayerSlot source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        IPlayerSlot[] slots = _slotsAccessor();
        if ((uint)index >= (uint)slots.Length) return;

        IPlayerSlot target = slots[index];
        target.Name = source.Name;
        target.SideIndex = source.SideIndex;
        target.ColorIndex = source.ColorIndex;
        target.TeamIndex = source.TeamIndex;
        target.StartIndex = source.StartIndex;
        target.AiLevel = source.AiLevel;
        target.IsAi = source.IsAi;
        target.IsHumanLocal = source.IsHumanLocal;

        if (target is ICnCNetPlayerSlot cncTarget && source is ICnCNetPlayerSlot cncSource)
        {
            cncTarget.IsHost = cncSource.IsHost;
            cncTarget.Ready = cncSource.Ready;
            cncTarget.AutoReady = cncSource.AutoReady;
            cncTarget.Ping = cncSource.Ping;
            cncTarget.Port = cncSource.Port;
        }
    }

    /// <inheritdoc />
    public void WriteSlot(int index, in SlotFieldUpdate update)
    {
        if (update.IsEmpty) return;

        IPlayerSlot[] slots = _slotsAccessor();
        if ((uint)index >= (uint)slots.Length) return;

        WriteSlotSilent(index, in update);
        _onChanged?.Invoke();
    }

    /// <inheritdoc />
    public void WriteSlotSilent(int index, in SlotFieldUpdate update)
    {
        if (update.IsEmpty) return;

        IPlayerSlot[] slots = _slotsAccessor();
        if ((uint)index >= (uint)slots.Length) return;

        IPlayerSlot s = slots[index];
        if (update.Name != null) s.Name = update.Name;
        if (update.SideIndex.HasValue) s.SideIndex = update.SideIndex.Value;
        if (update.ColorIndex.HasValue) s.ColorIndex = update.ColorIndex.Value;
        if (update.TeamIndex.HasValue) s.TeamIndex = update.TeamIndex.Value;
        if (update.StartIndex.HasValue) s.StartIndex = update.StartIndex.Value;
        if (update.AiLevel.HasValue) s.AiLevel = update.AiLevel.Value;
        if (update.IsAi.HasValue) s.IsAi = update.IsAi.Value;
        if (update.IsHumanLocal.HasValue) s.IsHumanLocal = update.IsHumanLocal.Value;

        if (s is ICnCNetPlayerSlot cnc)
        {
            if (update.IsHost.HasValue) cnc.IsHost = update.IsHost.Value;
            if (update.Ready.HasValue) cnc.Ready = update.Ready.Value;
            if (update.AutoReady.HasValue) cnc.AutoReady = update.AutoReady.Value;
            if (update.Ping.HasValue) cnc.Ping = update.Ping.Value;
            if (update.Port.HasValue) cnc.Port = update.Port.Value;
        }
    }

    /// <inheritdoc />
    public void ClearSlot(int index)
    {
        IPlayerSlot[] slots = _slotsAccessor();
        if ((uint)index >= (uint)slots.Length) return;

        ClearSlotCore(slots[index]);
        _onChanged?.Invoke();
    }

    /// <inheritdoc />
    public void ClearAll()
    {
        foreach (IPlayerSlot s in _slotsAccessor())
            ClearSlotCore(s);
        _onChanged?.Invoke();
    }

    /// <inheritdoc />
    public void CopyFrom(IReadOnlyList<IPlayerSlot> source)
    {
        IPlayerSlot[] slots = _slotsAccessor();
        for (int i = 0; i < slots.Length; i++)
        {
            if (i < source.Count)
                OverwriteSlotSilent(i, source[i]);
            else
                ClearSlotCore(slots[i]);
        }
        _onChanged?.Invoke();
    }

    private static void ClearSlotCore(IPlayerSlot s)
    {
        s.Name = string.Empty;
        s.IsAi = false;
        s.IsHumanLocal = false;
        s.SideIndex = 0;
        s.ColorIndex = 0;
        s.StartIndex = 0;
        s.TeamIndex = 0;
        s.AiLevel = 0;

        if (s is ICnCNetPlayerSlot cnc)
        {
            cnc.IsHost = false;
            cnc.Ready = false;
            cnc.AutoReady = false;
            cnc.Ping = -1;
            cnc.Port = 0;
        }
    }
}
