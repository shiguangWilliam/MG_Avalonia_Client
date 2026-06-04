namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>Fallback for windows that only rely on CommonWindowBehaviors back buttons for now.</summary>
public static class StubWindowBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        // Intentionally empty — CommonWindowBehaviors covers btnCancel/btnReturnToMenu/etc.
    }
}
