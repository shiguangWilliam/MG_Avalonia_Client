using ClientUpdater;

namespace ClientAvalonia.GlobalState.Updater;

/// <summary>
/// 版本检查与自更新接口。
///
/// 作用：Updater 是 static class，无法 mock。OptionsWindow 的"检查更新"
/// 按钮和启动期版本检查都直接调 Updater，迁移到接口后可注入 fake
/// 模拟"有更新 / 无更新 / 检查失败"三种场景。
/// </summary>
public interface IUpdater
{
    /// <summary>当前游戏版本号。</summary>
    string GameVersion { get; }

    /// <summary>自定义更新组件清单（call-in 自更新）。</summary>
    IReadOnlyList<CustomComponent> CustomComponents { get; }

    /// <summary>本地文件版本检查完成事件。</summary>
    event Action? OnLocalFileVersionsChecked;

    /// <summary>初始化 Updater（启动期由 PreStartup 调用）。</summary>
    void Initialize(
        string gamePath,
        string baseResourcePath,
        string settingsIniName,
        string localGame,
        string callingExecutable);

    /// <summary>触发本地文件版本检查（异步，结果通过事件回报）。</summary>
    void CheckLocalFileVersions();
}
