namespace ClientAvalonia.Core;

/// <summary>UI follow-up requested by picker interaction logic (no Avalonia types).</summary>
public enum WorkspacePickerUiRequest
{
    None = 0,

    /// <summary>Open a folder picker, then call <see cref="WorkspacePickerController.CompleteRegisterFromFolder"/>.</summary>
    BrowseFolderForRegister,
}

/// <summary>Outcome of a picker command (register / launch / …).</summary>
public sealed class WorkspacePickerCommandResult
{
    public bool Succeeded { get; init; }

    public WorkspacePickerUiRequest UiRequest { get; init; }

    public string StatusText { get; init; } = string.Empty;

    public static WorkspacePickerCommandResult Ok(string status) => new()
    {
        Succeeded = true,
        UiRequest = WorkspacePickerUiRequest.None,
        StatusText = status,
    };

    public static WorkspacePickerCommandResult Fail(string status) => new()
    {
        Succeeded = false,
        UiRequest = WorkspacePickerUiRequest.None,
        StatusText = status,
    };

    public static WorkspacePickerCommandResult RequestBrowse(string status) => new()
    {
        Succeeded = false,
        UiRequest = WorkspacePickerUiRequest.BrowseFolderForRegister,
        StatusText = status,
    };
}
