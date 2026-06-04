using ClientAvalonia.IniUi.Loading;

namespace ClientAvalonia.IniUi.Ast;

public static class IniAstBuilder
{
    public static IniFileAst BuildFromFile(string path)
    {
        IniDocument document = IniDocument.Load(path);
        IReadOnlySet<string> overlaySections = ParseOverlaySectionNames(path);
        return new IniFileAst(path, document, overlaySections);
    }

    private static HashSet<string> ParseOverlaySectionNames(string path)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IniSection section in IniDocument.ParseOverlay(path).Sections)
            names.Add(section.SectionName);
        return names;
    }
}
