using System.Globalization;
using Avalonia.Media;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Schema;

namespace ClientAvalonia.IniUi.Loading;

public sealed class PropertyResolver
{
    private readonly ControlRegistry _registry;
    private readonly ILocalizationService _localization;

    public PropertyResolver(ControlRegistry registry, ILocalizationService localization)
    {
        _registry = registry;
        _localization = localization;
    }

    public void ApplySectionAttributes(UiNode node, IniSection section, string? windowName)
    {
        foreach (KeyValuePair<string, string> kvp in section.Keys)
        {
            string key = kvp.Key;
            string value = kvp.Value;

            if (key.StartsWith("$CC", StringComparison.Ordinal))
                continue;

            node.RawAttributes[key] = value;

            string schemaKey = IniKeyAliases.Normalize(key);
            IniPropertyDefinition? propDef = _registry.FindProperty(node.ControlType, schemaKey);
            if (propDef == null)
                continue;

            ApplyKnownProperty(node, windowName, propDef, value);
        }
    }

    private void ApplyKnownProperty(UiNode node, string? windowName, IniPropertyDefinition def, string rawValue)
    {
        string value = def.Localizable
            ? _localization.Localize(windowName, node.Id, def.Key, rawValue, notify: def.Kind != IniPropertyKind.Expression)
            : rawValue;

        switch (def.Kind)
        {
            case IniPropertyKind.String:
                SetProp(node, def, value);
                break;
            case IniPropertyKind.Int:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                    SetProp(node, def, i);
                break;
            case IniPropertyKind.Bool:
                SetProp(node, def, IniConversions.BooleanFromString(value, false));
                break;
            case IniPropertyKind.Size:
                ParseSize(node, value);
                break;
            case IniPropertyKind.Location:
                ParseLocation(node, value);
                break;
            case IniPropertyKind.RgbaColor:
            case IniPropertyKind.RgbColor:
                SetProp(node, def, ParseColor(value, def.Kind == IniPropertyKind.RgbaColor));
                break;
            case IniPropertyKind.TexturePath:
            case IniPropertyKind.SoundPath:
            case IniPropertyKind.Url:
            case IniPropertyKind.Enum:
            case IniPropertyKind.CommaList:
                SetProp(node, def, value);
                break;
            case IniPropertyKind.Expression:
                if (!LayoutResolver.NeedsExpression(value)
                    && int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int plain))
                    SetProp(node, def, (double)plain);
                break;
            case IniPropertyKind.Opaque:
                break;
        }
    }

    private static void ParseSize(UiNode node, string value)
    {
        string[] parts = value.Split(',');
        if (parts.Length != 2)
            return;

        node.Props["Width"] = ParseDouble(parts[0]);
        node.Props["Height"] = ParseDouble(parts[1]);
    }

    private static void ParseLocation(UiNode node, string value)
    {
        string[] parts = value.Split(',');
        if (parts.Length != 2)
            return;

        node.Props["CanvasLeft"] = ParseDouble(parts[0]);
        node.Props["CanvasTop"] = ParseDouble(parts[1]);
    }

    private static double ParseDouble(string s)
        => double.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out double d) ? d : 0;

    private static void SetProp(UiNode node, IniPropertyDefinition def, object val)
    {
        string name = def.AvaloniaPropName ?? def.Key;
        node.Props[name] = val;
    }

    private static Color ParseColor(string value, bool hasAlpha)
    {
        string[] parts = value.Split(',');
        if (parts.Length < 3)
            return Colors.Transparent;

        byte r = byte.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
        byte g = byte.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
        byte b = byte.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
        byte a = hasAlpha && parts.Length > 3
            ? byte.Parse(parts[3].Trim(), CultureInfo.InvariantCulture)
            : (byte)255;

        return Color.FromArgb(a, r, g, b);
    }
}
