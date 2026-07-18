namespace ClientAvalonia.Core;

/// <summary>Origin of a workspace candidate in the Avalonia picker.</summary>
public enum ModRegistryEntrySource
{
    /// <summary>HKCU\SOFTWARE\ClientAvalonia\ModWorkspaces\{ModName}.</summary>
    AvaloniaRegistry,

    /// <summary>User browsed a folder this session (not yet written, or write disabled).</summary>
    Manual,

    /// <summary>Read-only hint from DX SOFTWARE\{ModName}\InstallPath — never written by Avalonia.</summary>
    LegacyDxHint,
}

public enum ModRegistryEntryState
{
    Ready,
    Stale,
    Missing,
}

/// <summary>One workspace candidate for the multi-mod picker.</summary>
public sealed class ModRegistryEntry
{
    public ModRegistryEntry(
        string modName,
        string? installPath,
        ModRegistryEntryState state,
        ModRegistryEntrySource source = ModRegistryEntrySource.AvaloniaRegistry,
        string? displayName = null)
    {
        ModName = modName;
        InstallPath = installPath;
        State = state;
        Source = source;
        DisplayName = displayName ?? modName;
    }

    public string ModName { get; }

    public string? InstallPath { get; }

    public ModRegistryEntryState State { get; }

    public ModRegistryEntrySource Source { get; }

    public string DisplayName { get; }

    public bool IsReady => State == ModRegistryEntryState.Ready;

    public string StatusLabel => State switch
    {
        ModRegistryEntryState.Ready => "Ready",
        ModRegistryEntryState.Stale => "Stale",
        _ => "未注册",
    };

    public string SourceLabel => Source switch
    {
        ModRegistryEntrySource.Manual => "手动",
        ModRegistryEntrySource.LegacyDxHint => "DX 提示",
        _ => "Avalonia",
    };
}
