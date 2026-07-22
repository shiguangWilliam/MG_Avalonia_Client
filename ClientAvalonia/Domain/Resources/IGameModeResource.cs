namespace ClientAvalonia.Domain.Resources;

/// <summary>
/// 游戏模式资源。
///
/// 作用：替代直接依赖 GameModeEntry。字段从 ClientAvalonia/Domain/GameModeEntry.cs
/// 反推。默认实现：GameModeEntry : IGameModeResource。
/// </summary>
public interface IGameModeResource : IResource
{
    /// <summary>模式内部名（逻辑主键）。对应 GameModeEntry.Name。</summary>
    string Name { get; }

    /// <summary>未本地化 UI 名。对应 GameModeEntry.UntranslatedUIName。</summary>
    string UntranslatedUIName { get; }

    /// <summary>地图代码 INI 名。对应 GameModeEntry.MapCodeIniName。</summary>
    string MapCodeIniName { get; }

    /// <summary>是否仅多人。对应 GameModeEntry.MultiplayerOnly。</summary>
    bool MultiplayerOnly { get; }
}
