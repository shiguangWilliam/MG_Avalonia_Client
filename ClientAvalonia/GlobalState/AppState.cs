using ClientAvalonia.CnCNet;
using ClientAvalonia.Configuration;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.GlobalState.Updater;
using ClientAvalonia.Lan;
using ClientAvalonia.Services;

namespace ClientAvalonia.GlobalState;

/// <summary>
/// Typed shortcuts over <see cref="EnvironmentServices"/> for production call sites.
///
/// Replaces direct <c>ProgramConstants.*</c> / <c>ClientConfiguration.Instance</c> /
/// <c>CnCNetSessionService.Instance</c> / <c>GameResourceCatalog.Instance</c> reads.
/// Tests inject mocks via <see cref="EnvironmentServices.Register{T}"/> + <see cref="EnvironmentServices.Reset"/>.
///
/// When no factory is registered (unit tests that skip PreStartup), falls back to the
/// production adapters so call sites remain usable without every fixture registering DI.
/// </summary>
public static class AppState
{
    public static IGameEnvironment Environment
        => EnvironmentServices.TryResolve<IGameEnvironment>() ?? new ProgramConstantsGameEnvironment();

    public static IGameConfiguration Configuration
        => EnvironmentServices.TryResolve<IGameConfiguration>() ?? new ClientConfigurationAdapter();

    public static ICnCNetSession CnCNet
        => EnvironmentServices.TryResolve<ICnCNetSession>() ?? new CnCNetSessionServiceAdapter();

    /// <summary>LAN lobby/session facade (parallel to <see cref="CnCNet"/>).</summary>
    public static ILanSession Lan
        => EnvironmentServices.TryResolve<ILanSession>() ?? LanSessionAccessor.Current;

    public static IResourceCatalog Resources
        => EnvironmentServices.TryResolve<IResourceCatalog>()
           ?? new GameResourceCatalogAdapter(GameResourceCatalog.Instance);

    public static IUpdater Updater
        => EnvironmentServices.TryResolve<IUpdater>() ?? new UpdaterAdapter();

    public static IMultiplayerColorCatalog Colors
        => EnvironmentServices.TryResolve<IMultiplayerColorCatalog>()
           ?? new MultiplayerColorCatalogAdapter();
}
