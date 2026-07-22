namespace ClientAvalonia.Domain.Resources;

/// <summary>
/// 地图 / 游戏模式 / 任务资源目录接口。
///
/// 作用：GameResourceCatalog 是 sealed 类，无法继承做 mock。本接口
/// 把它解密封装，返回 IMapResource / IMissionResource / IGameModeResource
/// 而非具体 DTO，让 LobbyAction / ChangeMapAction / MapListBindingApplier
/// 可以注入测试用 catalog，并为在线下载资源预留扩展点。
/// </summary>
public interface IResourceCatalog
{
    /// <summary>所有已加载的地图（来自 MPMaps.ini + 自定义 map 目录扫描）。</summary>
    IReadOnlyList<IMapResource> Maps { get; }

    /// <summary>所有游戏模式（Standard, Custom, ...）。</summary>
    IReadOnlyList<IGameModeResource> GameModes { get; }

    /// <summary>所有任务（Campaign / Mission）。</summary>
    IReadOnlyList<IMissionResource> Missions { get; }

    /// <summary>资源加载完成事件。UI 用于触发首次列表渲染。</summary>
    event Action? Loaded;

    /// <summary>确保资源已加载（幂等）。首次调用触发磁盘扫描。</summary>
    void EnsureLoaded();

    /// <summary>根据 dropdown filter index（0=favorites, 1+=mode）取对应 GameMode。</summary>
    IGameModeResource? GetGameModeForFilterIndex(int filterIndex);

    /// <summary>根据 filter index 取该模式下的地图列表。</summary>
    IReadOnlyList<IMapResource> GetMapsForFilterIndex(int filterIndex);

    /// <summary>在给定地图列表中随机选一个（按玩家数过滤）。</summary>
    int PickRandomMapIndex(IReadOnlyList<IMapResource> visible, int playerCount = 2);

    /// <summary>切换地图的"收藏"状态并持久化。</summary>
    bool ToggleFavoriteMap(IMapResource map, IGameModeResource? gameMode);

    /// <summary>取所有收藏的地图。</summary>
    IReadOnlyList<IMapResource> GetFavoriteMaps();
}
