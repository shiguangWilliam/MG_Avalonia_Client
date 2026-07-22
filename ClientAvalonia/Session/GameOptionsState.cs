namespace ClientAvalonia.Session;

/// <summary>
/// 可变游戏选项状态，实现 <see cref="IGameOptionsState"/>。
/// </summary>
public sealed class GameOptionsState : IGameOptionsState
{
    public List<bool> CheckBoxValues { get; set; } = [];

    public List<int> DropDownIndices { get; set; } = [];

    public bool MapOfficial { get; set; }

    public string MapSha1 { get; set; } = string.Empty;

    public string GameModeName { get; set; } = string.Empty;

    public int FrameSendRate { get; set; }

    public int MaxAhead { get; set; }

    public int ProtocolVersion { get; set; }

    public int RandomSeed { get; set; }

    public bool RemoveStartingLocations { get; set; }

    public string MapUntranslatedName { get; set; } = string.Empty;

    IReadOnlyList<bool> IGameOptionsState.CheckBoxValues => CheckBoxValues;

    IReadOnlyList<int> IGameOptionsState.DropDownIndices => DropDownIndices;
}
