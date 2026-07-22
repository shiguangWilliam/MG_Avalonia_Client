using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using FluentAssertions;
using Xunit;
using System;
using System.IO;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Locks <see cref="IniDocument"/> BasedOn + $BaseSection semantics, mirroring
/// ClientCore.CCIniFile:
///   - INISystem.BasedOn chains multiple base files (comma-separated).
///   - $THEME_DIR$ in BasedOn expands to the directory of the child INI.
///   - Overlay sections/keys merge on top of base sections/keys (overlay wins per key).
///   - $BaseSection within a section pulls in absent keys from a sibling section
///     (used for variant panels like lbChatMessages_Player deriving from lbChatMessages).
///   - ParseOverlay skips BasedOn entirely (used for hot-reload diffs).
/// </summary>
public sealed class IniDocumentTests
{
    private static string TempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ini-doc-{Guid.NewGuid():N}.ini");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void GetSection_Is_Case_Insensitive()
    {
        string path = TempFile("[Window]\nKey=value\n");
        try
        {
            IniDocument doc = IniDocument.Load(path);
            doc.GetSection("WINDOW").Should().NotBeNull();
            doc.GetSection("window").Should().NotBeNull();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GetStringValue_Is_Case_Insensitive_On_Key()
    {
        string path = TempFile("[Window]\nMyKey=value\n");
        try
        {
            IniDocument.Load(path).GetStringValue("Window", "MYKEY", "fallback").Should().Be("value");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Comments_And_Blank_Lines_Are_Skipped()
    {
        string path = TempFile("""
            ; line comment
            # hash comment

            [Window]
            Key=value ; inline comment
            """);
        try
        {
            IniDocument doc = IniDocument.Load(path);
            doc.GetStringValue("Window", "Key", "fb").Should().Be("value");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BasedOn_Chain_Merges_Base_Sections()
    {
        string basePath = TempFile("""
            [Base]
            Shared=base
            OnlyInBase=base-only
            """);
        string childPath = TempFile($"""
            [INISystem]
            BasedOn={Path.GetFileName(basePath)}

            [Base]
            Shared=child

            [Child]
            Key=child-value
            """);
        try
        {
            IniDocument doc = IniDocument.Load(childPath);

            doc.GetStringValue("Base", "Shared", "fb").Should().Be("child", "overlay wins per key");
            doc.GetStringValue("Base", "OnlyInBase", "fb").Should().Be("base-only", "missing key inherits from base");
            doc.GetStringValue("Child", "Key", "fb").Should().Be("child-value", "child-only section preserved");
        }
        finally { File.Delete(basePath); File.Delete(childPath); }
    }

    [Fact]
    public void BasedOn_Chain_Handles_Multiple_Files()
    {
        string base1 = TempFile("[A]\nK1=v1\n");
        string base2 = TempFile("[B]\nK2=v2\n");
        string child = TempFile($"""
            [INISystem]
            BasedOn={Path.GetFileName(base1)},{Path.GetFileName(base2)}

            [Child]
            K=child
            """);
        try
        {
            IniDocument doc = IniDocument.Load(child);
            doc.GetStringValue("A", "K1", "fb").Should().Be("v1");
            doc.GetStringValue("B", "K2", "fb").Should().Be("v2");
            doc.GetStringValue("Child", "K", "fb").Should().Be("child");
        }
        finally { File.Delete(base1); File.Delete(base2); File.Delete(child); }
    }

    [Fact]
    public void Theme_Dir_Placeholder_Expands()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"theme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string baseIni = Path.Combine(dir, "Base.ini");
        File.WriteAllText(baseIni, "[S]\nK=v\n");
        string child = Path.Combine(dir, "Child.ini");
        File.WriteAllText(child, """
            [INISystem]
            BasedOn=$THEME_DIR$/Base.ini

            [Other]
            X=1
            """);
        try
        {
            IniDocument doc = IniDocument.Load(child);
            doc.GetStringValue("S", "K", "fb").Should().Be("v");
            doc.GetStringValue("Other", "X", "fb").Should().Be("1");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void BaseSection_Pulls_Absent_Keys_From_Sibling()
    {
        string path = TempFile("""
            [Window]

            [Template]
            Width=400
            Height=300
            Color=Red

            [Variant]
            $BaseSection=Template
            Height=100
            """);
        try
        {
            IniDocument doc = IniDocument.Load(path);
            IniSection? variant = doc.GetSection("Variant");

            variant.Should().NotBeNull();
            variant!.GetStringValue("Width", "fb").Should().Be("400", "absent key pulled from base");
            variant.GetStringValue("Color", "fb").Should().Be("Red");
            variant.GetStringValue("Height", "fb").Should().Be("100", "own key wins over base");
            variant.GetStringValue("$BaseSection", "fb").Should().Be("Template", "$BaseSection itself stays in the merged section");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseOverlay_Skips_BasedOn_Chain()
    {
        string basePath = TempFile("[Base]\nK=base\n");
        string overlay = TempFile($"""
            [INISystem]
            BasedOn={Path.GetFileName(basePath)}

            [Overlay]
            K=overlay
            """);
        try
        {
            IniDocument doc = IniDocument.ParseOverlay(overlay);
            doc.GetSection("Base").Should().BeNull("ParseOverlay does not merge BasedOn");
            doc.GetStringValue("Overlay", "K", "fb").Should().Be("overlay");
        }
        finally { File.Delete(basePath); File.Delete(overlay); }
    }

    [Fact]
    public void Save_Roundtrips_Sections_And_Keys()
    {
        string path = TempFile("");
        try
        {
            var doc = new IniDocument();
            doc.SetStringValue("A", "K", "V");
            doc.SetIntValue("A", "N", 42);
            doc.SetBooleanValue("A", "Flag", true);
            doc.Save(path);

            IniDocument reloaded = IniDocument.ParseOverlay(path);
            reloaded.GetStringValue("A", "K", "fb").Should().Be("V");
            reloaded.GetIntValue("A", "N", -1).Should().Be(42);
            reloaded.GetBooleanValue("A", "Flag", false).Should().BeTrue();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_Section_Returns_Default()
    {
        string path = TempFile("[Window]\nKey=value\n");
        try
        {
            IniDocument doc = IniDocument.Load(path);
            doc.GetStringValue("Missing", "K", "default").Should().Be("default");
            doc.GetIntValue("Missing", "K", -7).Should().Be(-7);
            doc.GetBooleanValue("Missing", "K", true).Should().BeTrue();
        }
        finally { File.Delete(path); }
    }
}
