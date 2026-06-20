namespace ClientAvalonia.Core;

/// <summary>Client startup parameters (DXMainClient <c>StartupParams</c>).</summary>
public readonly struct StartupParams
{
    public StartupParams(bool noAudio, bool multipleInstanceMode, IReadOnlyList<string> unknownParams)
    {
        NoAudio = noAudio;
        MultipleInstanceMode = multipleInstanceMode;
        UnknownStartupParams = unknownParams;
    }

    public bool NoAudio { get; }

    public bool MultipleInstanceMode { get; }

    public IReadOnlyList<string> UnknownStartupParams { get; }
}
