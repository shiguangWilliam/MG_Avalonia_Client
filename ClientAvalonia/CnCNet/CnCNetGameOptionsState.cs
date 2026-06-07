using System.Collections.Generic;

namespace ClientAvalonia.CnCNet;

/// <summary>Game options payload for GO CTCP (XNA CnCNetGameLobby.OnGameOptionChanged / ApplyGameOptions).</summary>
public sealed class CnCNetGameOptionsState
{
    public IReadOnlyList<bool> CheckBoxValues { get; init; } = [];

    public IReadOnlyList<int> DropDownIndices { get; init; } = [];

    public bool MapOfficial { get; init; }

    public string MapSha1 { get; init; } = string.Empty;

    public string GameModeName { get; init; } = string.Empty;

    public int FrameSendRate { get; init; }

    public int MaxAhead { get; init; }

    public int ProtocolVersion { get; init; }

    public int RandomSeed { get; init; }

    public bool RemoveStartingLocations { get; init; }

    public string MapUntranslatedName { get; init; } = string.Empty;
}
