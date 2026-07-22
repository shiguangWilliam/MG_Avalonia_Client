using ClientAvalonia.Domain;

namespace ClientAvalonia.IniUi.Actions.Lobby;

/// <summary>
/// Sets a player slot's color. State-only mutation; the executor refreshes UI
/// and IRC broadcast after this returns.
/// </summary>
public sealed class SetPlayerColorAction : LobbyAction
{
    private readonly int _slotIndex;
    private readonly int _colorIndex;
    private int _previousColorIndex;

    public SetPlayerColorAction(int slotIndex, int colorIndex)
    {
        _slotIndex = slotIndex;
        _colorIndex = colorIndex;
    }

    public override string DisplayName => $"Set player {_slotIndex} color → {_colorIndex}";

    public override void Execute(LobbyActionContext ctx)
    {
        if ((uint)_slotIndex >= (uint)LobbyPlayerSlot.MaxSlots)
            return;
        if (_slotIndex >= ctx.Game.PlayerSlots.Count)
            return;

        _previousColorIndex = ctx.Game.PlayerSlots[_slotIndex].ColorIndex;
        ctx.Game.PlayerSlots[_slotIndex].ColorIndex = _colorIndex;
    }

    public override void Undo(LobbyActionContext ctx)
    {
        if ((uint)_slotIndex >= (uint)LobbyPlayerSlot.MaxSlots)
            return;
        if (_slotIndex >= ctx.Game.PlayerSlots.Count)
            return;

        ctx.Game.PlayerSlots[_slotIndex].ColorIndex = _previousColorIndex;
    }
}
