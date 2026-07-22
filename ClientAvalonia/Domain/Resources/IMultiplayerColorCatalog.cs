using ClientAvalonia.Services;

namespace ClientAvalonia.Domain.Resources;

/// <summary>
/// 多人游戏颜色目录接口。
///
/// 作用：后补的 DefaultAiSlotPolicy.AutoFillToMapCapacity 调用
/// MultiplayerColorCatalog.Load() 来确定 ColorIndex 上限。当前 Load() 是
/// static method + 静态缓存，跨测试会污染。抽接口后，测试可注入
/// 一个稳定的 8 色目录，避免缓存串扰。
///
/// 注意：本接口只提供"颜色有哪些"；具体槽位选了哪个颜色由
/// IPlayerSlot.ColorIndex 在 Session 内承载，不要把分配状态塞进目录。
/// </summary>
public interface IMultiplayerColorCatalog
{
    /// <summary>所有多人游戏颜色（按 GameOptions.ini [MPColors] 顺序）。</summary>
    IReadOnlyList<MultiplayerColorCatalog.MultiplayerColorEntry> Load();
}
