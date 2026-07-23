using ClientAvalonia.Services;

namespace ClientAvalonia.Domain.Resources;

/// <summary>
/// 将 <see cref="MultiplayerColorCatalog"/> 静态 Load 适配为 <see cref="IMultiplayerColorCatalog"/>。
/// </summary>
public sealed class MultiplayerColorCatalogAdapter : IMultiplayerColorCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<MultiplayerColorCatalog.MultiplayerColorEntry> Load()
        => MultiplayerColorCatalog.Load();
}
