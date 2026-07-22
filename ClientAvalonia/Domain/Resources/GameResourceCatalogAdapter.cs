using ClientAvalonia.Domain;
using ClientAvalonia.Services;

namespace ClientAvalonia.Domain.Resources;

/// <summary>
/// 将 <see cref="GameResourceCatalog"/> 适配为 <see cref="IResourceCatalog"/>。
/// MapEntry / MissionEntry / GameModeEntry 已直接实现资源接口。
/// </summary>
public sealed class GameResourceCatalogAdapter : IResourceCatalog
{
    private readonly GameResourceCatalog _catalog;

    public GameResourceCatalogAdapter(GameResourceCatalog catalog)
        => _catalog = catalog;

    /// <inheritdoc />
    public IReadOnlyList<IMapResource> Maps => _catalog.Maps;

    /// <inheritdoc />
    public IReadOnlyList<IGameModeResource> GameModes => _catalog.GameModes;

    /// <inheritdoc />
    public IReadOnlyList<IMissionResource> Missions => _catalog.Missions;

    /// <inheritdoc />
    public event Action? Loaded
    {
        add => _catalog.Loaded += value;
        remove => _catalog.Loaded -= value;
    }

    /// <inheritdoc />
    public void EnsureLoaded() => _catalog.EnsureLoaded();

    /// <inheritdoc />
    public IGameModeResource? GetGameModeForFilterIndex(int filterIndex)
        => _catalog.GetGameModeForFilterIndex(filterIndex);

    /// <inheritdoc />
    public IReadOnlyList<IMapResource> GetMapsForFilterIndex(int filterIndex)
        => _catalog.GetMapsForFilterIndex(filterIndex);

    /// <inheritdoc />
    /// <remarks>
    /// A1 fix: previously this method threw <see cref="ArgumentException"/> if any
    /// element was not a <see cref="MapEntry"/>, which broke the
    /// <see cref="IResourceCatalog"/> contract (any <see cref="IMapResource"/> should
    /// be acceptable). Now delegates to the interface-typed overload on
    /// <see cref="GameResourceCatalog"/> so mock implementations work in tests.
    /// </remarks>
    public int PickRandomMapIndex(IReadOnlyList<IMapResource> visible, int playerCount = 2)
        => _catalog.PickRandomMapIndex(visible, playerCount);

    /// <inheritdoc />
    /// <remarks>
    /// A1 fix: previously this method threw <see cref="ArgumentException"/> if
    /// <paramref name="map"/> was not a <see cref="MapEntry"/>. Now delegates to
    /// the interface-typed overload on <see cref="GameResourceCatalog"/>.
    /// </remarks>
    public bool ToggleFavoriteMap(IMapResource map, IGameModeResource? gameMode)
        => _catalog.ToggleFavoriteMap(map, gameMode);

    /// <inheritdoc />
    public IReadOnlyList<IMapResource> GetFavoriteMaps() => _catalog.GetFavoriteMaps();
}
