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
    // 槽位核心共享经 GameSessionBase（实例状态需类载体，扩展方法不适用——
    // 见 note/architecture-issue-list-2026-08-25.md 附二，2026-08-31 决策）。
}
