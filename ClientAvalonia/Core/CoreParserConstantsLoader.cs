using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientCore;

namespace ClientAvalonia.Core;

/// <summary>ParserConstants via unified loader (GlobalThemeSettings + ClientConfiguration).</summary>
public static class CoreParserConstantsLoader
{
    public static IReadOnlyDictionary<string, int> Load()
    {
        if (!ClientCoreBootstrap.IsInitialized)
            return DefaultParserConstants.Create();

        return ParserConstantsLoader.LoadForGame(ProgramConstants.GamePath);
    }
}
