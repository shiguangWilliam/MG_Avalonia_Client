using ClientAvalonia.Core;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Binding;

public sealed class UiBindingSession
{
    private readonly IUserSettingsStore _settings;
    private readonly IUiStateService _state;
    private IReadOnlyList<SettingBindingEntry> _settingEntries = [];

    public UiBindingSession(ClientEnvironment environment)
    {
        ClientCoreBootstrap.TryEnsureInitialized(environment.GameRoot, out _);
        _settings = ClientCoreBootstrap.IsInitialized
            ? new ClientCoreSettingsStore()
            : new UserIniSettingsStore(environment);
        _state = new UiStateService(environment);
    }

    public IUserSettingsStore Settings => _settings;

    public IUiStateService State => _state;

    public int SettingBindingCount => _settingEntries.Count;

    public void ApplyToTree(UiNodeViewModel root, string windowName)
    {
        _settingEntries = SettingBindingApplier.Apply(root, _settings);
        StateBindingApplier.Apply(root, _state, windowName);
    }

    public void CommitSettings()
        => SettingBindingApplier.Commit(_settingEntries, _settings);

    public void DiscardSettings()
        => _settings.Reload();
}
