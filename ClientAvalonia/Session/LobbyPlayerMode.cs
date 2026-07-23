namespace ClientAvalonia.Session;

/// <summary>
/// 大厅玩家模式（遭遇战 vs 多人）。
///
/// 设计理由（见 docs/design/layered-architecture.md §1）：
/// <list type="bullet">
/// <item>本质上是 <see cref="IGameSession.Mode"/> 的取值范围——属于 Session 抽象层。</item>
/// <item>Phase 5 P5-4：从 <c>ClientAvalonia.Services</c> 命名空间迁移到 <c>Session</c> 命名空间，
/// 与 <see cref="IGameSession"/> / <see cref="IPlayerSlot"/> / <see cref="IPlayerSlotSink"/> 同处。</item>
/// <item>旧位置（<c>ClientAvalonia.Services.LobbyPlayerMode</c>）已删除；所有引用文件加
/// <c>using ClientAvalonia.Session;</c>。</item>
/// </list>
/// </summary>
public enum LobbyPlayerMode
{
    /// <summary>遭遇战（本地单人）。</summary>
    Skirmish,

    /// <summary>多人（CnCNet / LAN）。</summary>
    Multiplayer,
}
