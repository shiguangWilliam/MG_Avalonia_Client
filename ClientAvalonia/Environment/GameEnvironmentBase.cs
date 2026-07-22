namespace ClientAvalonia.GlobalState.Environment;

/// <summary>
/// IGameEnvironment 的默认基类。
///
/// 作用：把"派生路径"逻辑（ResourcesPath / BaseResourcesPath）抽到基类，
/// 让子类只需要提供核心抽象属性，其余路径自动派生。
/// </summary>
public abstract class GameEnvironmentBase : IGameEnvironment
{
    /// <inheritdoc />
    public abstract string LocalGame { get; }

    /// <inheritdoc />
    public abstract string GamePath { get; }

    /// <inheritdoc />
    public abstract string PlayerName { get; }

    /// <inheritdoc />
    public abstract string GameVersion { get; }

    /// <inheritdoc />
    public virtual IReadOnlyList<string> AiPlayerNames { get; } = Array.Empty<string>();

    /// <inheritdoc />
    public virtual IReadOnlyList<string> TeamNames { get; } = ["A", "B", "C", "D"];

    /// <inheritdoc />
    public virtual string ResourcesPath => Path.Combine(GamePath, "Resources");

    /// <inheritdoc />
    public virtual string BaseResourcesPath => Path.Combine(ResourcesPath, "Base");
}
