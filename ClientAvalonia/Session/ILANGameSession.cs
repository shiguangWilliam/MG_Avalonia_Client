namespace ClientAvalonia.Session;

/// <summary>
/// 局域网游戏会话 = 局域网遭遇战 + Host，无 Tunnel。
///
/// 作用：与 ICnCNetGameSession 平级，共享 ISkirmishSession，但不依赖
/// CnCNet Tunnel / IRC。当前 Avalonia LAN 路径尚未完整，接口先预留。
/// </summary>
public interface ILANGameSession : ISkirmishSession
{
    /// <summary>房主名。</summary>
    string HostName { get; }

    /// <summary>本机是否房主。</summary>
    bool IsHost { get; }
}
