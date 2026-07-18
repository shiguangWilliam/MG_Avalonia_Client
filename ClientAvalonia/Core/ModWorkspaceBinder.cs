using ClientAvalonia.Services;
using ClientCore;
using ClientCore.Enums;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>
/// Binds / tears down the Avalonia multi-mod workspace (GameRoot + session).
/// </summary>
public static class ModWorkspaceBinder
{
    public static bool IsBound { get; private set; }

    public static string? CurrentModName { get; private set; }

    public static string? CurrentInstallPath { get; private set; }

    public static string? CurrentClientGameType { get; private set; }

    /// <summary>
    /// Bind workspace and run ClientCore + Startup bootstrap.
    /// Call only after early PreStartup (logger/culture); not while another workspace is live
    /// without <see cref="TeardownSession"/> first.
    /// </summary>
    /// <param name="clientGameType">
    /// Manual picker selection (TS/YR/Ares/RA). Used as <see cref="ClientTypeHelper.SessionFallback"/>
    /// when ClientDefinitions.ini lacks ClientGameType; never rewrites the ini on disk.
    /// </param>
    public static bool TryBindAndBootstrap(
        string modName,
        string installPath,
        string clientGameType,
        out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(modName))
        {
            error = "Mod 名称不能为空。";
            return false;
        }

        if (!ModWorkspaceRegistry.IsKnownClientGameType(clientGameType))
        {
            error = $"请选择 ClientGameType（可选：{string.Join(", ", ModWorkspaceRegistry.ClientGameTypeOptions)}）。";
            return false;
        }

        string normalized = installPath.TrimEnd('\\', '/');
        if (!ModWorkspaceRegistry.IsInstallPathValid(normalized))
        {
            error = "路径无效：目录下必须存在 Resources\\ClientDefinitions.ini。";
            return false;
        }

        if (IsBound)
            TeardownSession();

        // Must be set before ClientCoreBootstrap reads ClientConfiguration.ClientGameType.
        if (!TryApplySessionClientGameType(clientGameType, out string? typeError))
        {
            error = typeError;
            return false;
        }

        Environment.CurrentDirectory = normalized;
        ProgramConstants.SetHostedGameRoot(normalized);
        CurrentModName = modName.Trim();
        CurrentInstallPath = normalized;

        // Persist selection into Avalonia registry (session store; does not touch DX keys / ini).
        ModWorkspaceRegistry.TryWriteClientGameType(CurrentModName, CurrentClientGameType!, out _);

        ClientLogService.EnsureGameRootInitialized();

        if (!ClientCoreBootstrap.TryEnsureInitialized(normalized, out error))
        {
            ClearRuntimeMarks();
            return false;
        }

        try
        {
            var startup = new Startup();
            startup.Execute();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Logger.Log($"ModWorkspaceBinder: Startup.Execute failed: {ex}");
            ClearRuntimeMarks();
            ClientCoreBootstrap.ResetForWorkspaceRebind();
            return false;
        }

        if (!Startup.BootstrapSucceeded)
        {
            error = Startup.BootstrapError ?? "Startup bootstrap failed.";
            ClearRuntimeMarks();
            ClientCoreBootstrap.ResetForWorkspaceRebind();
            return false;
        }

        IsBound = true;
        ModWorkspaceLastSelection.Save(CurrentModName!, CurrentInstallPath!);
        Logger.Log(
            $"ModWorkspaceBinder: bound workspace '{CurrentModName}' -> '{CurrentInstallPath}' (ClientGameType={CurrentClientGameType}).");
        return true;
    }

    /// <summary>
    /// Sets <see cref="ClientTypeHelper.SessionFallback"/> from a picker label.
    /// Safe to unit-test without running full Startup bootstrap.
    /// </summary>
    public static bool TryApplySessionClientGameType(string clientGameType, out string? error)
    {
        error = null;
        string? normalizedType = ModWorkspaceRegistry.NormalizeClientGameType(clientGameType);
        if (normalizedType == null
            || !ClientTypeHelper.TryParse(normalizedType, out ClientType fallbackType))
        {
            error = $"请选择 ClientGameType（可选：{string.Join(", ", ModWorkspaceRegistry.ClientGameTypeOptions)}）。";
            return false;
        }

        ClientTypeHelper.SessionFallback = fallbackType;
        CurrentClientGameType = normalizedType;
        return true;
    }

    /// <summary>
    /// §5.3 session cleanup before returning to the workspace picker.
    /// Does not open UI; caller replaces MainWindow with the picker.
    /// </summary>
    public static void TeardownSession()
    {
        Logger.Log("ModWorkspaceBinder: tearing down workspace session.");

        try
        {
            CnCNetSessionService.Instance.Disconnect();
        }
        catch (Exception ex)
        {
            Logger.Log($"ModWorkspaceBinder: CnCNet disconnect failed: {ex.Message}");
        }

        try
        {
            GameResourceCatalog.Instance.Invalidate();
        }
        catch (Exception ex)
        {
            Logger.Log($"ModWorkspaceBinder: catalog invalidate failed: {ex.Message}");
        }

        ClientCoreBootstrap.ResetForWorkspaceRebind();
        Startup.ResetBootstrapState();
        ClearRuntimeMarks();
        ProgramConstants.ClearHostedGameRoot();
        IsBound = false;
    }

    private static void ClearRuntimeMarks()
    {
        ClientTypeHelper.ClearSessionFallback();
        CurrentModName = null;
        CurrentInstallPath = null;
        CurrentClientGameType = null;
        IsBound = false;
    }
}
