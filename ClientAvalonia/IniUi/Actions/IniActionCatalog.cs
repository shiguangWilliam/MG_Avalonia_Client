using System;
using System.Collections.Generic;
using System.Linq;

using ClientAvalonia.IniUi.Behaviors;
using Rampastring.Tools;

namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// <see cref="IIniActionCatalog"/> 的默认实现：大小写不敏感的字符串→委托表。
///
/// Threading：注册通常在启动期完成，派发可并发。这里用简单的字典 + lock，
/// 因为热度极低（一次点击一个 lookup），不值得用并发字典。
/// </summary>
public sealed class IniActionCatalog : IIniActionCatalog
{
    private readonly Dictionary<string, Action<string, IUiNavigationHost>> _handlers
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _registrationOrder = new();

    /// <inheritdoc />
    public void Register(string actionName, Action<string, IUiNavigationHost> handler)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            throw new ArgumentException("Action name must not be null/empty/whitespace.", nameof(actionName));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        string key = actionName.Trim();
        lock (_handlers)
        {
            if (!_handlers.ContainsKey(key))
                _registrationOrder.Add(key);
            _handlers[key] = handler;
        }
    }

    /// <inheritdoc />
    public bool TryDispatch(string actionRawValue, IUiNavigationHost host)
    {
        if (string.IsNullOrWhiteSpace(actionRawValue))
            return false;

        // 解析「name[:args]」格式（冒号后的全部作为 args 字符串传入）。
        // 与 DX INI 习惯一致——动作名后跟冒号分隔的参数。
        // 名字部分 trim；参数部分保持原样（让 handler 决定是否再 trim）。
        string name = IniActionName.ParseName(actionRawValue);
        string args = IniActionName.ParseArgs(actionRawValue);

        if (string.IsNullOrWhiteSpace(name))
            return false;

        Action<string, IUiNavigationHost>? handler;
        lock (_handlers)
        {
            if (!_handlers.TryGetValue(name, out handler))
                return false;
        }

        try
        {
            handler(args, host);
            Logger.Log($"[IniAction] dispatched '{name}' args='{args}'");
            return true;
        }
        catch (Exception ex)
        {
            // 不向上传播：BehaviorRegistry / 控件点击不应因 Action 内部异常而崩 UI。
            Logger.Log($"[IniAction] '{name}' threw: {ex}");
            return true; // 仍认为命中（动作确实存在并执行了，只是失败了）
        }
    }

    /// <inheritdoc />
    public bool IsRegistered(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            return false;
        lock (_handlers)
            return _handlers.ContainsKey(actionName.Trim());
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> RegisteredNames
    {
        get
        {
            lock (_handlers)
                return _registrationOrder.ToList();
        }
    }
}
