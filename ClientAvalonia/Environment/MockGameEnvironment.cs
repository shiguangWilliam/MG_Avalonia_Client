namespace ClientAvalonia.GlobalState.Environment;

/// <summary>
/// 测试用可变环境实现。通过 *Value 属性赋值，只读接口属性转发。
/// </summary>
public sealed class MockGameEnvironment : GameEnvironmentBase
{
    public string LocalGameValue { get; set; } = "mg";

    public string GamePathValue { get; set; } = @"C:\fake\mg";

    public string PlayerNameValue { get; set; } = "TestPlayer";

    public string GameVersionValue { get; set; } = "1.0.0";

    public IReadOnlyList<string> AiPlayerNamesValue { get; set; } =
        ["Easy AI", "Medium AI", "Hard AI"];

    public IReadOnlyList<string> TeamNamesValue { get; set; } =
        ["A", "B", "C", "D"];

    public override string LocalGame => LocalGameValue;

    public override string GamePath => GamePathValue;

    public override string PlayerName => PlayerNameValue;

    public override string GameVersion => GameVersionValue;

    public override IReadOnlyList<string> AiPlayerNames => AiPlayerNamesValue;

    public override IReadOnlyList<string> TeamNames => TeamNamesValue;
}
