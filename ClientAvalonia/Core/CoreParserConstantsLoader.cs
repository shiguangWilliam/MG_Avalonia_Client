using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientCore;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Core;

/// <summary>ParserConstants via unified loader (GlobalThemeSettings + ClientConfiguration).</summary>
public static class CoreParserConstantsLoader
{
    public static IReadOnlyDictionary<string, int> Load()
    {
        if (!ClientCoreBootstrap.IsInitialized)
            return DefaultParserConstants.Create();

        return ParserConstantsLoader.LoadForGame(AppState.Environment.GamePath);
    }
}
