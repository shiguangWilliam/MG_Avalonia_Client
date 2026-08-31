using ClientAvalonia.Session;

namespace ClientAvalonia.Domain;

/// <summary>
/// 单个玩家 / AI 槽位（与 XNA PlayerInfo 子集对齐）。
///
/// 同时实现 <see cref="IPlayerSlot"/> 与 <see cref="ICnCNetPlayerSlot"/>，
/// 这样 Skirmish 与 CnCNet 可共用同一个具体类——Skirmish 路径不读 CnCNet 字段，
/// CnCNet 路径读写额外的 Ready / Ping / Port。
/// </summary>
public sealed class LobbyPlayerSlot : IPlayerSlot, ICnCNetPlayerSlot
{
    /// <summary>
    /// 大厅玩家槽上限。DX 基线为 8；MG 分支提升至 9（9 人图 / 人类+8 AI）。
    /// 出生点映射（StartIndexToCombo）与加载 clamp 依赖此值同步放宽。
    /// </summary>
    public const int MaxSlots = 9;

    public string Name { get; set; } = string.Empty;

    public int SideIndex { get; set; }

    public int ColorIndex { get; set; }

    public int StartIndex { get; set; }

    public int TeamIndex { get; set; }

    public int AiLevel { get; set; } = 2;

    public bool IsAi { get; set; }

    public bool IsOccupied => !string.IsNullOrWhiteSpace(Name);

    public bool IsHumanLocal { get; set; }

    // ---- ICnCNetPlayerSlot ----

    /// <summary>CTCP PO 中 host 标记。Skirmish 路径不读。</summary>
    public bool IsHost { get; set; }

    /// <summary>本机玩家是否已准备（CTCP READY）。Skirmish 路径不读。</summary>
    public bool Ready { get; set; }

    /// <summary>是否启用自动准备（chkAutoReady）。Skirmish 路径不读。</summary>
    public bool AutoReady { get; set; }

    /// <summary>网络延迟（毫秒）。-1 = 未知。Skirmish 路径不读。</summary>
    public int Ping { get; set; } = -1;

    /// <summary>NAT 端口（tunnel 分配）。Skirmish 路径不读。</summary>
    public ushort Port { get; set; }

    public void Clear()
    {
        Name = string.Empty;
        IsAi = false;
        IsHumanLocal = false;
        SideIndex = 0;
        ColorIndex = 0;
        StartIndex = 0;
        TeamIndex = 0;
        AiLevel = 0;
        IsHost = false;
        Ready = false;
        AutoReady = false;
        Ping = -1;
        Port = 0;
    }

    public LobbyPlayerSlot Clone()
        => new()
        {
            Name = Name,
            SideIndex = SideIndex,
            ColorIndex = ColorIndex,
            StartIndex = StartIndex,
            TeamIndex = TeamIndex,
            AiLevel = AiLevel,
            IsAi = IsAi,
            IsHumanLocal = IsHumanLocal,
            IsHost = IsHost,
            Ready = Ready,
            AutoReady = AutoReady,
            Ping = Ping,
            Port = Port,
        };
}
