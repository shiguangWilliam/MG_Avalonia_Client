namespace ClientAvalonia.Session;

/// <summary>
/// 玩家槽位"写入端"接口——把所有外部对槽位的修改集中到一个收口。
///
/// 设计理由（见 <c>docs/design/layered-architecture.md</c> §2.2 / §3）：
/// <list type="bullet">
/// <item><see cref="IPlayerSlot"/> 的属性都是 { get; set; }，外部任意代码都能写，
/// 难以追踪。Sink 让写入路径变得显式且可观测（事件、日志、审计）。</item>
/// <item>配合 <see cref="IGameSession.PlayerSlots"/> 的只读视图，构成
/// "写入经 sink / 读取经 slots"的 CQRS-like 模式。</item>
/// <item>实现可加日志、广播、撤销栈等横切关注，不影响读路径性能。</item>
/// <item>"静默模式"用于批量应用（如 <c>PlayerOptionsCodec.ApplyDto</c> 收到 PO 时
/// 一次性写多槽位），避免触发多次 <see cref="IGameSession.StateChanged"/>。</item>
/// </list>
///
/// 依赖方向：Session 层接口，不知道 View / Service。
/// </summary>
public interface IPlayerSlotSink
{
    /// <summary>
    /// 覆盖整个槽位（用于 PO ApplyDto / Skirmish 默认装载）。
    /// 写完触发 Session 的状态变更通知。
    /// </summary>
    /// <param name="index">槽位索引（0..MaxSlots-1）。</param>
    /// <param name="source">数据来源槽位（仅复制字段值，不持有引用）。</param>
    void OverwriteSlot(int index, IPlayerSlot source);

    /// <summary>
    /// 静默覆盖整个槽位——不触发 <see cref="IGameSession.StateChanged"/>。
    /// 用于批量应用场景；调用方负责在所有写入完成后手动触发一次状态变更。
    /// </summary>
    void OverwriteSlotSilent(int index, IPlayerSlot source);

    /// <summary>
    /// 按字段增量更新单个槽位。
    /// </summary>
    void WriteSlot(int index, in SlotFieldUpdate update);

    /// <summary>静默版本——不触发状态变更。</summary>
    void WriteSlotSilent(int index, in SlotFieldUpdate update);

    /// <summary>清空指定槽位（等价于写一个未占用的空槽）。</summary>
    void ClearSlot(int index);

    /// <summary>清空所有槽位。</summary>
    void ClearAll();

    /// <summary>
    /// 从其他槽位列表批量复制（用于切换 Session 时迁移 / 默认装载）。
    /// 长度不匹配时按目标长度截断或清空多余槽位。
    /// </summary>
    void CopyFrom(IReadOnlyList<IPlayerSlot> source);
}

/// <summary>
/// 单字段增量更新包——只更新显式给值的字段，其余保持原样。
///
/// 设计：struct + nullable 字段，缺省值（null）表示"不更新此字段"。
/// 这样调用方可精确表达"只改 ColorIndex 不动其他"的语义。
/// </summary>
public readonly struct SlotFieldUpdate
{
    // 基础字段（IPlayerSlot）
    public string? Name { get; init; }
    public int? SideIndex { get; init; }
    public int? ColorIndex { get; init; }
    public int? TeamIndex { get; init; }
    public int? StartIndex { get; init; }
    public int? AiLevel { get; init; }
    public bool? IsAi { get; init; }
    public bool? IsHumanLocal { get; init; }

    // CnCNet 专用（仅在 ICnCNetPlayerSlot 上生效）
    public bool? IsHost { get; init; }
    public bool? Ready { get; init; }
    public bool? AutoReady { get; init; }
    public int? Ping { get; init; }
    public ushort? Port { get; init; }

    /// <summary>是否有任何字段需要更新（用于短路）。</summary>
    public bool IsEmpty
        => Name == null && SideIndex == null && ColorIndex == null
           && TeamIndex == null && StartIndex == null && AiLevel == null
           && IsAi == null && IsHumanLocal == null && IsHost == null
           && Ready == null && AutoReady == null && Ping == null && Port == null;

    /// <summary>便捷工厂：仅改 SideIndex / ColorIndex / TeamIndex / StartIndex（典型 UI 改槽）。</summary>
    public static SlotFieldUpdate Options(int? side = null, int? color = null,
        int? team = null, int? start = null)
        => new() { SideIndex = side, ColorIndex = color, TeamIndex = team, StartIndex = start };
}
