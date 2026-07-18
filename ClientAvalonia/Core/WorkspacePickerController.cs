using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ClientAvalonia.Core;

public delegate bool TryModRegisterOp(
    string modName,
    string installPath,
    string clientGameType,
    out string? error);

public delegate bool TryModNameOp(string modName, out string? error);

public delegate bool TryBindOp(
    string modName,
    string installPath,
    string clientGameType,
    out string? error);

/// <summary>
/// Interaction logic for the workspace picker (no Avalonia UI types).
/// Covers refresh / register / clear / probe / launch / close-gate sequencing.
/// </summary>
public sealed class WorkspacePickerController
{
    private readonly Func<IReadOnlyList<ModRegistryEntry>> _enumerate;
    private readonly Func<ModWorkspaceLastSelection.Snapshot?> _loadLast;
    private readonly TryModRegisterOp _tryRegister;
    private readonly TryModNameOp _tryClear;
    private readonly Func<IReadOnlyList<string>?, List<string>?, int> _tryCleanupDx;
    private readonly Func<string?, string?> _tryProbe;
    private readonly Func<string, string> _suggestModName;
    private readonly Func<string, string, ModRegistryEntry> _createManual;
    private readonly Func<string?, string?, string?> _resolveGameTypeHint;
    private readonly TryBindOp _tryBind;

    public WorkspacePickerController(
        Func<IReadOnlyList<ModRegistryEntry>>? enumerate = null,
        Func<ModWorkspaceLastSelection.Snapshot?>? loadLast = null,
        TryModRegisterOp? tryRegister = null,
        TryModNameOp? tryClear = null,
        Func<IReadOnlyList<string>?, List<string>?, int>? tryCleanupDx = null,
        Func<string?, string?>? tryProbe = null,
        Func<string, string>? suggestModName = null,
        Func<string, string, ModRegistryEntry>? createManual = null,
        Func<string?, string?, string?>? resolveGameTypeHint = null,
        TryBindOp? tryBind = null)
    {
        _enumerate = enumerate ?? (() => ModRegistryCatalog.Enumerate());
        _loadLast = loadLast ?? ModWorkspaceLastSelection.TryLoad;
        _tryRegister = tryRegister ?? ModRegistrar.TryRegister;
        _tryClear = tryClear ?? ModRegistrar.TryClear;
        _tryCleanupDx = tryCleanupDx ?? ModRegistrar.TryCleanupOrphanLegacyDxKeys;
        _tryProbe = tryProbe ?? (_ => ModRegistryCatalog.TryProbeLocalGameRoot());
        _suggestModName = suggestModName ?? ModRegistryCatalog.SuggestModName;
        _createManual = createManual ?? ModRegistryCatalog.CreateManualEntry;
        _resolveGameTypeHint = resolveGameTypeHint ?? ModRegistryCatalog.ResolveClientGameTypeHint;
        _tryBind = tryBind ?? DefaultBind;
    }

    public ObservableCollection<ModRegistryEntry> Entries { get; } = new();

    public ModRegistryEntry? Selected { get; set; }

    public string ModNameText { get; set; } = string.Empty;

    /// <summary>Manual ClientGameType (TS/YR/Ares/RA). Default YR.</summary>
    public string ClientGameTypeText { get; set; } = "YR";

    public string StatusText { get; private set; } = string.Empty;

    /// <summary>Set true only after successful bind, before UI raises WorkspaceBound.</summary>
    public bool AllowCloseAfterBind { get; private set; }

    public bool UserRequestedExit { get; private set; }

    /// <param name="preserveInstallPath">After register/clear, re-select by path (stable across refresh).</param>
    /// <param name="preserveModName">Prefer this ModName when multiple entries share a path.</param>
    public void Refresh(string? preserveInstallPath = null, string? preserveModName = null)
    {
        IReadOnlyList<ModRegistryEntry> incoming = _enumerate();
        ModWorkspaceLastSelection.Snapshot? last = _loadLast();

        string? selectPath = preserveInstallPath?.TrimEnd('\\', '/')
            ?? last?.InstallPath.TrimEnd('\\', '/');
        string? selectMod = preserveModName?.Trim();

        MergeEntries(incoming);

        ModRegistryEntry? highlight = null;
        if (!string.IsNullOrWhiteSpace(selectPath))
        {
            foreach (ModRegistryEntry entry in Entries)
            {
                if (!PathMatches(entry.InstallPath, selectPath))
                    continue;

                if (!string.IsNullOrWhiteSpace(selectMod)
                    && entry.ModName.Equals(selectMod, StringComparison.OrdinalIgnoreCase))
                {
                    highlight = entry;
                    break;
                }

                highlight ??= entry;
            }
        }

        Selected = highlight ?? (Entries.Count > 0 ? Entries[0] : null);
        ApplySelectionFields(Selected);

        int ready = 0;
        int dxHints = 0;
        foreach (ModRegistryEntry e in Entries)
        {
            if (e.IsReady)
                ready++;
            if (e.Source == ModRegistryEntrySource.LegacyDxHint)
                dxHints++;
        }

        StatusText = ready > 0
            ? $"找到 {ready} 个不同路径的 Ready 工作区"
              + (dxHints > 0 ? $"（其中 {dxHints} 条来自 DX 只读提示，可「注册到 Avalonia 表」固化）" : "")
              + "。请显式选择后启动。"
            : "尚无 Ready 工作区。请浏览游戏根目录并注册到 Avalonia 表。";
    }

    /// <summary>
    /// Merge catalog into <see cref="Entries"/> without Clear() — avoids ListBox layout corruption.
    /// </summary>
    private void MergeEntries(IReadOnlyList<ModRegistryEntry> incoming)
    {
        var incomingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModRegistryEntry entry in incoming)
        {
            if (!string.IsNullOrWhiteSpace(entry.InstallPath))
                incomingPaths.Add(entry.InstallPath.TrimEnd('\\', '/'));
        }

        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            string? path = Entries[i].InstallPath;
            if (string.IsNullOrWhiteSpace(path)
                || !incomingPaths.Contains(path.TrimEnd('\\', '/')))
            {
                Entries.RemoveAt(i);
            }
        }

        foreach (ModRegistryEntry entry in incoming)
        {
            string? normalized = entry.InstallPath?.TrimEnd('\\', '/');
            int existingIndex = FindEntryIndex(normalized, entry.ModName);
            if (existingIndex >= 0)
            {
                if (!EntryEquals(Entries[existingIndex], entry))
                    Entries[existingIndex] = entry;
            }
            else
            {
                int insertAt = FindInsertIndex(entry);
                Entries.Insert(insertAt, entry);
            }
        }
    }

    private static bool PathMatches(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        return string.Equals(
            a.TrimEnd('\\', '/'),
            b.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }

    private int FindEntryIndex(string? installPath, string modName)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return -1;

        string path = installPath.TrimEnd('\\', '/');
        for (int i = 0; i < Entries.Count; i++)
        {
            ModRegistryEntry e = Entries[i];
            if (!PathMatches(e.InstallPath, path))
                continue;

            if (e.ModName.Equals(modName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        for (int i = 0; i < Entries.Count; i++)
        {
            if (PathMatches(Entries[i].InstallPath, path))
                return i;
        }

        return -1;
    }

    private int FindInsertIndex(ModRegistryEntry entry)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (CompareEntriesForSort(entry, Entries[i]) < 0)
                return i;
        }

        return Entries.Count;
    }

    private static int CompareEntriesForSort(ModRegistryEntry a, ModRegistryEntry b)
    {
        int readyCmp = (b.IsReady ? 1 : 0).CompareTo(a.IsReady ? 1 : 0);
        if (readyCmp != 0)
            return readyCmp;

        int sourceCmp = SourceRank(a.Source).CompareTo(SourceRank(b.Source));
        if (sourceCmp != 0)
            return sourceCmp;

        return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static int SourceRank(ModRegistryEntrySource source) => source switch
    {
        ModRegistryEntrySource.AvaloniaRegistry => 0,
        ModRegistryEntrySource.Manual => 1,
        _ => 2,
    };

    private static bool EntryEquals(ModRegistryEntry a, ModRegistryEntry b)
        => a.ModName == b.ModName
           && PathMatches(a.InstallPath, b.InstallPath)
           && a.State == b.State
           && a.Source == b.Source
           && a.DisplayName == b.DisplayName;

    /// <summary>Sync ModName / ClientGameType fields from the selected entry (ini → registry → keep current).</summary>
    public void ApplySelectionFields(ModRegistryEntry? entry)
    {
        if (entry == null)
            return;

        ModNameText = entry.ModName;
        string? hint = _resolveGameTypeHint(entry.ModName, entry.InstallPath);
        if (hint != null)
            ClientGameTypeText = hint;
        else if (!ModWorkspaceRegistry.IsKnownClientGameType(ClientGameTypeText))
            ClientGameTypeText = "YR";
    }

    public bool TryAddFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "无法解析所选文件夹路径。";
            return false;
        }

        string modName = string.IsNullOrWhiteSpace(ModNameText)
            ? _suggestModName(path)
            : ModNameText.Trim();
        ModNameText = modName;

        ModRegistryEntry manual = _createManual(modName, path);
        ReplaceSamePath(manual);
        Entries.Insert(0, manual);
        Selected = manual;
        ApplySelectionFields(manual);

        StatusText = manual.IsReady
            ? $"已添加手动项：{path}。可直接启动，或注册到 Avalonia 表。"
            : "所选目录缺少 Resources\\ClientDefinitions.ini。";
        return manual.IsReady;
    }

    public bool TryProbeLocal()
    {
        string? path = _tryProbe(null);
        if (path == null)
        {
            StatusText = "旁路探测未找到 ClientDefinitions.ini（已检查 CWD 与 exe 旁）。";
            return false;
        }

        return TryAddFolder(path);
    }

    /// <summary>
    /// Start register: if the list has no usable path, ask the UI to open a folder picker
    /// (this is what users expect from「注册到 Avalonia 表」).
    /// </summary>
    public WorkspacePickerCommandResult BeginRegister()
    {
        if (!TryGetRegisterablePath(out _))
        {
            StatusText = "请选择游戏根目录（含 Resources\\ClientDefinitions.ini）以注册到 Avalonia 表。";
            return WorkspacePickerCommandResult.RequestBrowse(StatusText);
        }

        return TryRegisterSelected()
            ? WorkspacePickerCommandResult.Ok(StatusText)
            : WorkspacePickerCommandResult.Fail(StatusText);
    }

    /// <summary>
    /// After folder picker: add/validate folder, then write Avalonia registry.
    /// </summary>
    public WorkspacePickerCommandResult CompleteRegisterFromFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "已取消选择目录。";
            return WorkspacePickerCommandResult.Fail(StatusText);
        }

        if (!TryAddFolder(path))
            return WorkspacePickerCommandResult.Fail(StatusText);

        return TryRegisterSelected()
            ? WorkspacePickerCommandResult.Ok(StatusText)
            : WorkspacePickerCommandResult.Fail(StatusText);
    }

    public bool TryRegisterSelected()
    {
        if (!TryGetRegisterablePath(out string? path) || path == null || Selected == null)
        {
            StatusText = "请先选择带有效路径的项，或点「注册到 Avalonia 表」浏览目录。";
            return false;
        }

        EnsureClientGameTypeOrDefault();
        string modName = string.IsNullOrWhiteSpace(ModNameText) ? Selected.ModName : ModNameText.Trim();
        string gameType = ClientGameTypeText.Trim();
        if (!_tryRegister(modName, path, gameType, out string? error))
        {
            StatusText = error ?? "注册失败。";
            return false;
        }

        string? normalized = ModWorkspaceRegistry.NormalizeClientGameType(gameType);
        StatusText = $"已写入 Avalonia 注册表：{ModWorkspaceRegistry.KeyPathFor(modName)}（ClientGameType={normalized}）";
        string registeredMsg = StatusText;
        Refresh(preserveInstallPath: path, preserveModName: modName);
        StatusText = registeredMsg;
        return true;
    }

    private bool TryGetRegisterablePath(out string? path)
    {
        path = Selected?.InstallPath;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        path = path.TrimEnd('\\', '/');
        return Selected!.IsReady || ModWorkspaceRegistry.IsInstallPathValid(path);
    }

    private void EnsureClientGameTypeOrDefault()
    {
        if (!ModWorkspaceRegistry.IsKnownClientGameType(ClientGameTypeText))
            ClientGameTypeText = "YR";
    }

    public bool TryClearSelected()
    {
        if (Selected is null)
        {
            StatusText = "请先选择一项。";
            return false;
        }

        if (Selected.Source == ModRegistryEntrySource.LegacyDxHint)
        {
            StatusText = "DX 提示项请用「清理无用 DX 脏键」（只清路径与 ModName 不符或失效的项，保留 DX 正用的键）。";
            return false;
        }

        string modName = string.IsNullOrWhiteSpace(ModNameText) ? Selected.ModName : ModNameText.Trim();
        string? clearedPath = Selected.InstallPath?.TrimEnd('\\', '/');
        if (!_tryClear(modName, out string? error))
        {
            StatusText = error ?? "清除失败。";
            return false;
        }

        StatusText = $"已清除 Avalonia 项：{modName}";
        string clearedMsg = StatusText;
        Refresh(preserveInstallPath: clearedPath);
        StatusText = clearedMsg;
        return true;
    }

    public bool TryCleanupOrphanDx()
    {
        var cleared = new List<string>();
        int count = _tryCleanupDx(null, cleared);
        if (count == 0)
        {
            Refresh();
            StatusText = "没有可清理的 DX 脏键（合法 DX 键已保留：路径的 RegistryInstallPath/LocalGame 与键名一致）。";
            return false;
        }

        Refresh();
        StatusText = $"已清理 {count} 个无用 DX InstallPath：{string.Join(", ", cleared)}";
        return true;
    }

    /// <summary>
    /// Launch sequencing: bind → set AllowCloseAfterBind → caller raises WorkspaceBound.
    /// </summary>
    public bool TryLaunchSelected()
    {
        AllowCloseAfterBind = false;

        if (Selected?.InstallPath is not { Length: > 0 } path)
        {
            StatusText = "请选择一个 Ready 工作区。";
            return false;
        }

        if (!Selected.IsReady)
        {
            StatusText = "所选路径无效（Stale/Missing），无法启动。";
            return false;
        }

        EnsureClientGameTypeOrDefault();
        string modName = string.IsNullOrWhiteSpace(ModNameText) ? Selected.ModName : ModNameText.Trim();
        string gameType = ClientGameTypeText.Trim();
        if (!_tryBind(modName, path, gameType, out string? error))
        {
            StatusText = error ?? "绑定工作区失败。";
            return false;
        }

        // Critical order: allow close BEFORE UI replaces MainWindow / closes picker.
        AllowCloseAfterBind = true;
        StatusText = $"已绑定：{modName} → {path}（ClientGameType={ModWorkspaceRegistry.NormalizeClientGameType(gameType) ?? gameType}）";
        return true;
    }

    public void MarkUserRequestedExit() => UserRequestedExit = true;

    public bool ShouldCancelClose(bool workspaceIsBound)
        => WorkspacePickerClosePolicy.ShouldCancelClose(AllowCloseAfterBind, UserRequestedExit, workspaceIsBound);

    private void ReplaceSamePath(ModRegistryEntry manual)
    {
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Entries[i].InstallPath, manual.InstallPath, StringComparison.OrdinalIgnoreCase))
                Entries.RemoveAt(i);
        }
    }

    private static bool DefaultBind(string mod, string path, string clientGameType, out string? error)
    {
        if (!ModWorkspaceBinder.TryBindAndBootstrap(mod, path, clientGameType, out error))
            return false;

        PreStartup.NotifyWorkspaceBound();
        return true;
    }
}

/// <summary>Picker window close policy (startup gate).</summary>
public static class WorkspacePickerClosePolicy
{
    public static bool ShouldCancelClose(bool allowCloseAfterBind, bool userRequestedExit, bool workspaceIsBound)
        => !(allowCloseAfterBind || userRequestedExit || workspaceIsBound);
}

/// <summary>
/// Ordered steps for return-to-picker / launch hand-off.
/// Unit tests assert the sequence; App must call in this order.
/// </summary>
public static class WorkspaceSessionHandoff
{
    public enum ReturnStep
    {
        EnsureExplicitShutdown,
        ShowPicker,
        ClosePreviousMainWindow,
        TeardownSession,
    }

    public enum LaunchHandOffStep
    {
        BindWorkspace,
        AllowPickerClose,
        RaiseWorkspaceBound,
        ShowMainWindow,
        ClosePicker,
    }

    public static IReadOnlyList<ReturnStep> ReturnToPickerOrder { get; } =
    [
        ReturnStep.EnsureExplicitShutdown,
        ReturnStep.ShowPicker,
        ReturnStep.ClosePreviousMainWindow,
        ReturnStep.TeardownSession,
    ];

    public static IReadOnlyList<LaunchHandOffStep> LaunchHandOffOrder { get; } =
    [
        LaunchHandOffStep.BindWorkspace,
        LaunchHandOffStep.AllowPickerClose,
        LaunchHandOffStep.RaiseWorkspaceBound,
        LaunchHandOffStep.ShowMainWindow,
        LaunchHandOffStep.ClosePicker,
    ];

    /// <summary>Execute return-to-picker with the required order (testable).</summary>
    public static void ExecuteReturnToPicker(
        Action ensureExplicitShutdown,
        Action showPicker,
        Action closePrevious,
        Action teardownSession)
    {
        ensureExplicitShutdown();
        showPicker();
        closePrevious();
        teardownSession();
    }

    public static List<string> TraceReturnToPicker(
        Action? onShow = null,
        Action? onClose = null,
        Action? onTeardown = null)
    {
        var trace = new List<string>();
        ExecuteReturnToPicker(
            () => trace.Add(nameof(ReturnStep.EnsureExplicitShutdown)),
            () =>
            {
                trace.Add(nameof(ReturnStep.ShowPicker));
                onShow?.Invoke();
            },
            () =>
            {
                trace.Add(nameof(ReturnStep.ClosePreviousMainWindow));
                onClose?.Invoke();
            },
            () =>
            {
                trace.Add(nameof(ReturnStep.TeardownSession));
                onTeardown?.Invoke();
            });
        return trace;
    }
}
