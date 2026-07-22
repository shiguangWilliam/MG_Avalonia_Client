using System;

namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// 解析 INI <c>$LeftClickAction</c> 字符串的简单工具。
///
/// 格式（与 DX 启动器一致）：
///   <c>Name</c> 或 <c>Name:arg1:arg2:...</c>
///
/// 第一个冒号前是动作名，之后是参数（参数本身可含冒号）。
/// </summary>
public static class IniActionName
{
    /// <summary>从原始 INI 值中解析出动作名（大小写不敏感的部分）。</summary>
    public static string ParseName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        string trimmed = raw.Trim();
        int colon = trimmed.IndexOf(':');
        // 名字部分也要 trim（"  Trim  :Arg" → "Trim"），与 INI 解析惯例一致。
        return (colon < 0 ? trimmed : trimmed[..colon]).Trim();
    }

    /// <summary>从原始 INI 值中解析出冒号后的参数字符串（无冒号则返回空）。</summary>
    /// <remarks>
    /// 参数保留原样（不 trim）——让 handler 决定是否再 trim。
    /// "Foo:  bar  " → "  bar  "。
    /// </remarks>
    public static string ParseArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        int colon = raw.IndexOf(':');
        return colon < 0 ? string.Empty : raw[(colon + 1)..];
    }

    /// <summary>判断是否为特殊的 <c>DISABLE</c> 动作（用于禁用整个 UI 容器）。</summary>
    public static bool IsDisable(string raw)
        => "DISABLE".Equals(ParseName(raw), StringComparison.OrdinalIgnoreCase);
}
