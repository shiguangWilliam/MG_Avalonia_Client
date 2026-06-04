namespace ClientAvalonia.IniUi.Models;

/// <summary>Strongly typed INI value kinds — see note/ini-ui-specification.md §4.</summary>
public enum IniPropertyKind
{
    String,
    Int,
    Bool,
    Size,
    Location,
    RgbaColor,
    RgbColor,
    TexturePath,
    SoundPath,
    Url,
    Enum,
    Expression,
    CommaList,
    Opaque,
}
