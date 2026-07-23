using ClientUpdater;

namespace ClientAvalonia.GlobalState.Updater;

/// <summary>
/// 将 <see cref="ClientUpdater.Updater"/> 静态类适配为 <see cref="IUpdater"/>。
/// </summary>
public sealed class UpdaterAdapter : IUpdater
{
    /// <summary>创建适配器并订阅底层 Updater 事件。</summary>
    public UpdaterAdapter()
        => global::ClientUpdater.Updater.OnLocalFileVersionsChecked += RaiseLocalFileVersionsChecked;

    /// <inheritdoc />
    public string GameVersion => global::ClientUpdater.Updater.GameVersion;

    /// <inheritdoc />
    public IReadOnlyList<CustomComponent> CustomComponents =>
        global::ClientUpdater.Updater.CustomComponents?.ToList() ?? [];

    /// <inheritdoc />
    public event Action? OnLocalFileVersionsChecked;

    /// <inheritdoc />
    public void Initialize(
        string gamePath,
        string baseResourcePath,
        string settingsIniName,
        string localGame,
        string callingExecutable)
        => global::ClientUpdater.Updater.Initialize(
            gamePath,
            baseResourcePath,
            settingsIniName,
            localGame,
            callingExecutable);

    /// <inheritdoc />
    public void CheckLocalFileVersions() => global::ClientUpdater.Updater.CheckLocalFileVersions();

    private void RaiseLocalFileVersionsChecked() => OnLocalFileVersionsChecked?.Invoke();
}
