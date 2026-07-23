using ClientCore;

namespace ClientAvalonia.GlobalState.Environment;

/// <summary>
/// 生产环境实现：从 ProgramConstants / ClientConfiguration 读取运行时环境。
/// </summary>
internal sealed class ProgramConstantsGameEnvironment : GameEnvironmentBase
{
    public override string LocalGame => ClientConfiguration.Instance.LocalGame;

    public override string GamePath => ProgramConstants.GamePath;

    public override string PlayerName => ProgramConstants.PLAYERNAME;

    public override string GameVersion => ProgramConstants.GAME_VERSION;

    public override IReadOnlyList<string> AiPlayerNames => ProgramConstants.AI_PLAYER_NAMES;

    public override IReadOnlyList<string> TeamNames => ProgramConstants.TEAMS;
}
