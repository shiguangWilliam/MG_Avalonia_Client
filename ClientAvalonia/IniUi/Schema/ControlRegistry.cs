namespace ClientAvalonia.IniUi.Schema;

public sealed class ControlRegistry
{
    private readonly Dictionary<string, ControlTypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ControlTypeDefinition definition)
    {
        _types[definition.IniTypeName] = definition;

        if (definition.Aliases == null)
            return;

        foreach (string alias in definition.Aliases)
            _types[alias] = definition;
    }

    public ControlTypeDefinition Resolve(string iniTypeName)
    {
        if (_types.TryGetValue(iniTypeName, out ControlTypeDefinition? def))
            return def;

        if (_types.TryGetValue("XNAPanel", out ControlTypeDefinition? panel))
            return panel with { IniTypeName = iniTypeName };

        throw new InvalidOperationException($"No control type registered and no XNAPanel fallback: {iniTypeName}");
    }

    public bool IsRegistered(string iniTypeName) => _types.ContainsKey(iniTypeName);

    public IReadOnlyCollection<ControlTypeDefinition> All => _types.Values.Distinct().ToList();

    public IniPropertyDefinition? FindProperty(string iniTypeName, string key)
    {
        ControlTypeDefinition def = Resolve(iniTypeName);
        return def.Properties.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}
