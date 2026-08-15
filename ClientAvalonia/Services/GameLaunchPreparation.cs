using ClientAvalonia.Domain;
using ClientCore;
using ClientCore.Extensions;
using ClientCore.I18N;
using ClientCore.Settings;
using Rampastring.Tools;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>
/// Pre-launch steps aligned with DXMainClient
/// <c>MainMenu.SharedUILogic_GameProcessStarting</c> + <c>OptionsWindow.RefreshSettings</c>.
/// </summary>
public static class GameLaunchPreparation
{
    internal const string PipelineVersion = "2025-06-19-r17";

    /// <summary>
    /// Idempotent subset run while players wait in a locked/ready room so START is less cold.
    /// Does not reload/save settings (those stay on the real launch path).
    /// </summary>
    public static void BeginLobbyPrewarm(CancellationToken cancellationToken)
    {
        Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            Logger.Log("GameLaunchPreparation: lobby prewarm started.");
            var sw = Stopwatch.StartNew();
            long translationMs = TimedStep("SyncTranslationFiles", () =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    SyncTranslationGameFilesIfNeeded();
            });
            long rendererMs = TimedStep("ApplyRenderer", () =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    GameRendererBootstrap.RefreshBeforeLaunch();
            });
            sw.Stop();
            if (!cancellationToken.IsCancellationRequested)
            {
                Logger.Log(
                    $"GameLaunchPreparation: lobby prewarm done in {sw.ElapsedMilliseconds} ms " +
                    $"(translation={translationMs}, renderer={rendererMs}).");
            }
        }, cancellationToken);
    }

    public static long PrepareForLaunch()
    {
        var sw = Stopwatch.StartNew();
        Logger.Log($"GameLaunchPreparation: pipeline {PipelineVersion}.");

        long reloadMs = TimedStep("ReloadSettings", () => UserINISettings.Instance.ReloadSettings());

        long rendererMs = TimedStep("ApplyRenderer", () =>
        {
            GameRendererBootstrap.RefreshBeforeLaunch();
            var manager = GameRendererBootstrap.Manager;
            Logger.Log(
                $"GameLaunchPreparation: renderer {manager.SelectedRenderer.InternalName}, " +
                $"UseQres={GameProcessLauncher.UseQres}, windowed={manager.GetEffectiveWindowedMode()} " +
                $"(UserINI.WindowedMode={UserINISettings.Instance.WindowedMode}).");
        });

        long translationMs = TimedStep("SyncTranslationFiles", SyncTranslationGameFilesIfNeeded);
        long saveMs = TimedStep("SaveSettings", () => UserINISettings.Instance.SaveSettings());

        sw.Stop();
        Logger.Log(
            $"GameLaunchPreparation: completed in {sw.ElapsedMilliseconds} ms " +
            $"(reload={reloadMs}, renderer={rendererMs}, translation={translationMs}, save={saveMs}).");
        return sw.ElapsedMilliseconds;
    }

    private static long TimedStep(string name, Action action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logger.Log($"GameLaunchPreparation: {name} failed: {ex.Message}");
        }

        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static void SyncTranslationGameFilesIfNeeded()
    {
        AppState.Configuration.Legacy.RefreshTranslationGameFiles();

        foreach (TranslationGameFile tgf in AppState.Configuration.Legacy.TranslationGameFiles)
        {
            string sourcePath = SafePath.CombineFilePath(UserINISettings.Instance.TranslationFolderPath, tgf.Source);
            string targetPath = SafePath.CombineFilePath(AppState.Environment.GamePath, tgf.Target);

            if (File.Exists(sourcePath))
            {
                if (FilesMatchByMetadata(sourcePath, targetPath))
                    continue;

                string sourceHash = Utilities.CalculateSHA1ForFile(sourcePath);
                string destinationHash = File.Exists(targetPath)
                    ? Utilities.CalculateSHA1ForFile(targetPath)
                    : string.Empty;

                if (sourceHash == destinationHash)
                    continue;

                FileExtensions.CreateHardLinkFromSource(sourcePath, targetPath);
                new FileInfo(targetPath).IsReadOnly = true;
            }
            else if (File.Exists(targetPath))
            {
                new FileInfo(targetPath).IsReadOnly = false;
                File.Delete(targetPath);
            }
        }
    }

    private static bool FilesMatchByMetadata(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath))
            return false;

        var source = new FileInfo(sourcePath);
        var target = new FileInfo(targetPath);
        return source.Length == target.Length
               && source.LastWriteTimeUtc == target.LastWriteTimeUtc;
    }
}
