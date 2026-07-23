namespace ClientAvalonia.IniUi.Behaviors;

public static class OptionsWindowBehaviors
{
    private static readonly string[] TabIds =
    [
        "btnTabDisplay", "btnTabAudio", "btnTabGame", "btnTabCnCNet",
        "btnTabSecurity", "btnTabUpdater", "btnTabComponents",
    ];

    public static void Register(BehaviorRegistry registry, IUiNavigationHost host)
    {
        for (int i = 0; i < TabIds.Length; i++)
        {
            int tab = i;
            registry.Register(TabIds[i], _ => host.SelectOptionsTab(tab));
        }

        registry.Register("tabControl", _ =>
            host.ShowStatus("Options: press 1–7 to switch tabs"));
    }
}
