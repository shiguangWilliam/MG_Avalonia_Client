using ClientAvalonia.Domain.Resources;
using ClientAvalonia.IniUi.Lobby;

namespace ClientAvalonia.IniUi.Actions.Lobby;

/// <summary>
/// Changes the skirmish/lobby map and (in single-player windows) refills AI
/// slots to match the new map's MaxPlayers via <see cref="DefaultAiSlotPolicy"/>.
/// </summary>
public sealed class ChangeMapAction : LobbyAction
{
    private readonly IMapResource _map;

    public ChangeMapAction(IMapResource map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
    }

    public override string DisplayName => $"Change map → {_map.DisplayName}";

    public override void Execute(LobbyActionContext ctx)
    {
        ctx.Game.Map = _map;

        if (!IsSinglePlayerWindow(ctx.WindowName ?? string.Empty))
            return;

        string playerName = ctx.Game.PlayerSlots.Count > 0 && ctx.Game.PlayerSlots[0].IsHumanLocal
            ? ctx.Game.PlayerSlots[0].Name
            : "Player";

        IReadOnlyList<string> aiNames = [];
        try
        {
            aiNames = GlobalState.Environment.EnvironmentServices
                .Resolve<Services.ILobbyCatalogService>().AiNames;
        }
        catch (InvalidOperationException)
        {
            // Tests may not register EnvironmentServices.
        }

        IMultiplayerColorCatalog colors =
            GlobalState.Environment.EnvironmentServices.Resolve<IMultiplayerColorCatalog>();

        try
        {
            playerName = GlobalState.Environment.EnvironmentServices
                .Resolve<GlobalState.Environment.IGameEnvironment>().PlayerName;
        }
        catch (InvalidOperationException)
        {
            // Tests may not register EnvironmentServices; keep slot/fallback name.
        }

        DefaultAiSlotPolicy.AutoFillToMapCapacity(
            ctx.Game,
            _map.MaxPlayers,
            playerName,
            colors,
            aiNames);
    }

    private static bool IsSinglePlayerWindow(string name)
        => name.Equals("SkirmishLobby", StringComparison.OrdinalIgnoreCase);
}
