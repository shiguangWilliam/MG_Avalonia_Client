using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientCore;
using Rampastring.Tools;
using System.IO;

namespace ClientAvalonia.Services;

/// <summary>Launch-time renderer and CnCNet diagnostics for comparing skirmish vs multiplayer paths.</summary>
public static class GameLaunchDiagnostics
{
    public static void LogLaunchStart(string launchMode, IGameLaunchSession session)
    {
        Logger.Log($"GameLaunchDiagnostics: mode={launchMode}, session={session.GetType().Name}.");
        LogRendererState("launch-start");
        LogCnCNetState("launch-start");
    }

    public static void LogAfterSpawnPrep(string launchMode, long elapsedMs)
    {
        Logger.Log($"GameLaunchDiagnostics: spawn-prep completed in {elapsedMs} ms (mode={launchMode}).");
    }

    public static void LogAfterPreparation(string launchMode, long elapsedMs)
    {
        Logger.Log($"GameLaunchDiagnostics: GameLaunchPreparation completed in {elapsedMs} ms (mode={launchMode}).");
        LogRendererState("post-preparation");
        LogCnCNetState("post-preparation");
    }

    public static void LogProcessStarted(string launchMode, long totalElapsedMs)
    {
        Logger.Log(
            $"GameLaunchDiagnostics: Process.Start returned in {totalElapsedMs} ms (mode={launchMode}). " +
            "Note: game window / Syringe injection time is NOT included — see syringe.log in game dir.");
        LogRendererState("process-started");
        LogCnCNetState("process-started");
    }

    public static void LogRendererState(string phase)
    {
        try
        {
            DirectDrawWrapperManager manager = GameRendererBootstrap.Manager;
            DirectDrawWrapper renderer = manager.SelectedRenderer;
            string ddrawPath = SafePath.CombineFilePath(ProgramConstants.GamePath, "ddraw.dll");
            string configPath = SafePath.CombineFilePath(ProgramConstants.GamePath, renderer.ConfigFileName);

            Logger.Log(
                $"GameLaunchDiagnostics [{phase}]: renderer={renderer.InternalName} ({renderer.UIName}), " +
                $"UseQres={GameProcessLauncher.UseQres}, windowed={manager.GetEffectiveWindowedMode()}, " +
                $"IsDummy={renderer.IsDummy}.");

            if (renderer.IsDummy)
            {
                Logger.Log($"GameLaunchDiagnostics [{phase}]: ddraw.dll not configured (stock DirectDraw).");
                return;
            }

            string resourceDll = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), renderer.DdrawDllResourcePath);
            Logger.Log($"GameLaunchDiagnostics [{phase}]: resource DLL={resourceDll}, exists={File.Exists(resourceDll)}.");

            if (!File.Exists(ddrawPath))
            {
                Logger.Log($"GameLaunchDiagnostics [{phase}]: ddraw.dll MISSING at {ddrawPath}.");
                return;
            }

            var ddrawInfo = new FileInfo(ddrawPath);
            string ddrawHash = Utilities.CalculateSHA1ForFile(ddrawPath);
            string? linkTarget = TryGetLinkTarget(ddrawPath);

            Logger.Log(
                $"GameLaunchDiagnostics [{phase}]: game ddraw.dll path={ddrawPath}, " +
                $"size={ddrawInfo.Length}, readOnly={ddrawInfo.IsReadOnly}, sha1={ddrawHash}" +
                (linkTarget != null ? $", linkTarget={linkTarget}" : string.Empty) + ".");

            if (File.Exists(resourceDll))
            {
                string resourceHash = Utilities.CalculateSHA1ForFile(resourceDll);
                bool hashMatch = string.Equals(ddrawHash, resourceHash, StringComparison.OrdinalIgnoreCase);
                Logger.Log(
                    $"GameLaunchDiagnostics [{phase}]: resource sha1={resourceHash}, " +
                    $"matchesGameDll={hashMatch}.");
            }

            if (File.Exists(configPath))
            {
                var ini = new IniFile(configPath);
                bool globalWindowed = ini.GetBooleanValue(renderer.WindowedModeSection, renderer.WindowedModeKey, false);
                string gameExe = ClientConfiguration.Instance.GetGameExecutableName();
                string spawnSection = Path.GetFileNameWithoutExtension(gameExe) + "-spawn";
                bool spawnWindowed = ini.GetBooleanValue(spawnSection, "windowed", globalWindowed);
                int spawnW = ini.GetIntValue(spawnSection, "width", 0);
                int spawnH = ini.GetIntValue(spawnSection, "height", 0);

                Logger.Log(
                    $"GameLaunchDiagnostics [{phase}]: {renderer.ConfigFileName} " +
                    $"[{renderer.WindowedModeSection}].windowed={globalWindowed}, " +
                    $"[{spawnSection}].windowed={spawnWindowed}, size={spawnW}x{spawnH}.");
            }
            else
            {
                Logger.Log($"GameLaunchDiagnostics [{phase}]: {renderer.ConfigFileName} missing at {configPath}.");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"GameLaunchDiagnostics [{phase}]: renderer log failed: {ex.Message}");
        }
    }

    public static void LogCnCNetState(string phase)
    {
        try
        {
            CnCNetSessionService svc = CnCNetSessionService.Instance;
            CnCNetIrcConnection? conn = svc.Connection;
            bool connected = conn is { IsConnected: true };
            bool connecting = conn is { IsConnecting: true };
            string? room = svc.ActiveGameRoom?.RoomName ?? svc.GameRoom?.Room.RoomName;
            int tunnelPing = svc.ActiveGameRoom?.Tunnel.PingInMs ?? -1;

            Logger.Log(
                $"GameLaunchDiagnostics [{phase}]: IRC connected={connected}, connecting={connecting}, " +
                $"IsInGame={ProgramConstants.IsInGame}, IsLaunchingGame={ProgramConstants.IsLaunchingGame}, room={room ?? "(none)"}, tunnelPing={tunnelPing} ms.");
        }
        catch (Exception ex)
        {
            Logger.Log($"GameLaunchDiagnostics [{phase}]: CnCNet log failed: {ex.Message}");
        }
    }

    private static string? TryGetLinkTarget(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.LinkTarget;
        }
        catch
        {
            return null;
        }
    }
}
