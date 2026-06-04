using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Binding;

public static class StateBindingApplier
{
    private static readonly Dictionary<string, Action<UiNodeViewModel, IUiStateService>> Bindings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["lblVersion"] = (vm, state) => vm.SetDisplayText(state.GameVersion),
            ["lblUpdateStatus"] = (vm, state) => vm.SetDisplayText(state.UpdateStatusText),
            ["lblCnCNetPlayerCount"] = (vm, state) => vm.SetDisplayText(state.OnlinePlayerCountText),
            ["btnLaunchGame"] = (vm, state) => vm.IsEnabled = state.CanLaunchGame,
        };

    public static void Apply(UiNodeViewModel root, IUiStateService state, string windowName)
    {
        if (windowName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
            state.RefreshMainMenuState();

        foreach (UiNodeViewModel vm in EnumerateTree(root))
        {
            if (Bindings.TryGetValue(vm.Id, out Action<UiNodeViewModel, IUiStateService>? bind))
                bind(vm, state);
        }
    }

    private static IEnumerable<UiNodeViewModel> EnumerateTree(UiNodeViewModel root)
    {
        yield return root;
        foreach (UiNodeViewModel child in root.Children)
        {
            foreach (UiNodeViewModel node in EnumerateTree(child))
                yield return node;
        }
    }
}
