namespace ClientAvalonia.Session;

/// <summary>
/// 遭遇战会话（单人本地）。
///
/// 作用：无网络元数据的本地对战。ICnCNetGameSession / ILANGameSession
/// 均继承此接口——"联网遭遇战 = 遭遇战 + 网络元数据"。
/// DefaultAiSlotPolicy / ChangeMapAction 应接收 ISkirmishSession。
/// </summary>
public interface ISkirmishSession : IGameSession
{
    // 单人本地，无额外网络元数据。
    // 共享逻辑（若有）用扩展方法，不用抽象基类（见 global-state-refactor.md §6.3）。

    /// <summary>
    /// 最近一次 <c>TryLoadSkirmishSettings</c> 读到的游戏选项快照
    /// （id → "True/False" | index；DX SkirmishLobby LoadSettings 的 [GameOptions]）。
    /// View 层负责把它回填到 UI；Session 本身不解析。
    /// </summary>
    IReadOnlyDictionary<string, string> LastLoadedGameOptions { get; }
}
