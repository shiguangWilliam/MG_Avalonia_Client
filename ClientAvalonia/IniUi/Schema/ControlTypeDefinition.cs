namespace ClientAvalonia.IniUi.Schema;

public sealed record IniPropertyDefinition(
    string Key,
    Models.IniPropertyKind Kind,
    bool Localizable = false,
    string? AvaloniaPropName = null);

public sealed record ControlTypeDefinition(
    string IniTypeName,
    string TemplateKey,
    string FallbackBaseType,
    IReadOnlyList<IniPropertyDefinition> Properties,
    IReadOnlyList<string>? Aliases = null);
