using ClientAvalonia.IniUi.Loading;

namespace ClientAvalonia.IniUi.Ast;

public enum IniEntryKind
{
    KeyValue,
    ChildControl,
}

public sealed class IniKeyValueAst
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public IniEntryKind Kind { get; init; } = IniEntryKind.KeyValue;
}

public sealed class IniSectionAst
{
    public required string Name { get; init; }
    public List<IniKeyValueAst> Entries { get; } = [];
}

/// <summary>Structural AST over merged INI document (M2: thin wrapper; M3+ may add transforms).</summary>
public sealed class IniFileAst
{
    public IniFileAst(string sourcePath, IniDocument document, IReadOnlySet<string> overlaySectionNames)
    {
        SourcePath = sourcePath;
        Document = document;
        OverlaySectionNames = overlaySectionNames;
    }

    public string SourcePath { get; }
    public IniDocument Document { get; }

    /// <summary>Section names declared in the overlay INI file (excludes BasedOn-only sections).</summary>
    public IReadOnlySet<string> OverlaySectionNames { get; }

    public IniSectionAst? GetSection(string name)
    {
        IniSection? section = Document.GetSection(name);
        if (section == null)
            return null;

        var ast = new IniSectionAst { Name = section.SectionName };
        foreach (KeyValuePair<string, string> kvp in section.Keys)
        {
            ast.Entries.Add(new IniKeyValueAst
            {
                Key = kvp.Key,
                Value = kvp.Value,
                Kind = kvp.Key.StartsWith("$CC", StringComparison.Ordinal) ? IniEntryKind.ChildControl : IniEntryKind.KeyValue,
            });
        }

        return ast;
    }
}
