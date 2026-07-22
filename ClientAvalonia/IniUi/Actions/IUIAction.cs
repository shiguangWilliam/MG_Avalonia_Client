using ClientAvalonia.Session;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// UI 动作的统一抽象——见 <c>docs/design/layered-architecture.md</c> §2.2。
///
/// 设计意图：
/// <list type="bullet">
/// <item>INI 派发（<c>$LeftClickAction=XXX</c>）不区分状态/命令——catalog 看到的只是字符串名 + 参数。</item>
/// <item>StateAction 与 CmdAction 的执行接口完全一样（都是 Execute），区别只在"调什么"。</item>
/// <item>用 <see cref="ActionKind"/> 标记区分语义，便于 catalog 路由（日志、限流、统计）。</item>
/// <item>实现建议：90% 的 action 用 <c>catalog.Register(name, kind, handler)</c> 工厂委托；
/// 仅当 action 携带大量配置（如 LaunchGame 带 mod 列表）才建具体 sealed class 实现 <see cref="IUIAction"/>。</item>
/// </list>
///
/// 不替换现有 <see cref="IIniActionCatalog"/>——本接口是它的"语义层补丁"，两者共存。
/// </summary>
public interface IUIAction
{
    /// <summary>动作名（大小写不敏感，与 INI 字符串对应）。</summary>
    string Name { get; }

    /// <summary>动作分类（路由依据）。</summary>
    ActionKind Kind { get; }

    /// <summary>
    /// 执行动作。返回结果（成功/失败/消息）；不抛异常。
    /// 实现内部捕获所有异常并打包到 <see cref="CmdResult"/>。
    /// </summary>
    CmdResult Execute(in UIActionContext context);
}

/// <summary>
/// 动作分类——见 layered-architecture.md §2.1 灰色地带判定规则。
/// </summary>
public enum ActionKind
{
    /// <summary>
    /// 状态变更：直接写 <see cref="IGameSession.SlotSink"/> 或 Session 公开 mutator。
    /// <list type="bullet">
    /// <item>幂等可逆</item>
    /// <item>纯数据修改</item>
    /// <item>不允许调用 Service</item>
    /// <item>触发 <see cref="IGameSession.StateChanged"/></item>
    /// </list>
    /// 典型：改色 / 改队 / 改 start / 选地图 / 改选项 checkbox
    /// </summary>
    State,

    /// <summary>
    /// 命令派发：调用 Service 层的方法。Service 内部读写 Session。
    /// <list type="bullet">
    /// <item>非幂等、有副作用</item>
    /// <item>不可逆（启动进程、发 KICK CTCP）</item>
    /// <item>触发 CmdResult 单播 + 可能间接触发 StateChanged</item>
    /// </list>
    /// 典型：启动游戏 / 踢人 / 退出 / 切窗口 / Lock Game
    /// </summary>
    Command,
}

/// <summary>
/// 命令执行结果（细粒度）。
///
/// 与 <see cref="IGameSession.StateChanged"/> 不同——StateChanged 是广播，CmdResult 是单播给发起者。
/// </summary>
public readonly struct CmdResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>提示消息（成功 toast / 失败原因）。</summary>
    public string? Message { get; init; }

    /// <summary>附加数据（如启动的 PID、跳转的目标窗口名）。</summary>
    public object? Data { get; init; }

    public static CmdResult Ok() => new() { Success = true };
    public static CmdResult Ok(string message) => new() { Success = true, Message = message };
    public static CmdResult Ok(string message, object? data) => new() { Success = true, Message = message, Data = data };
    public static CmdResult Fail(string reason) => new() { Success = false, Message = reason };

    /// <summary>便捷构造：从异常构造失败结果。</summary>
    public static CmdResult FromException(Exception ex)
        => new() { Success = false, Message = $"{ex.GetType().Name}: {ex.Message}" };
}
