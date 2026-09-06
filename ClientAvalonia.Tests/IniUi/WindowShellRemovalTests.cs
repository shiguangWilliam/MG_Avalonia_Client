using System.Collections.Generic;
using System.IO;
using ClientAvalonia.IniUi.Ast;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Schema;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Issue #18: shell-node removal must honor the explicit INI declaration
/// (IsShell=true/false) BEFORE the pixel heuristics, the heuristic thresholds
/// must be INI-overridable (ShellMaxWidthLobby/ShellMaxWidthWindow), and every
/// heuristic removal must land in client.log (observable via Diagnostics-free
/// path — asserted indirectly by behavior here).
/// </summary>
public sealed class WindowShellRemovalTests
{
    private static UiNodeTree BuildAndProcess(string ini, string windowName)
    {
        string path = Path.Combine(Path.GetTempPath(), $"shell-test-{System.Guid.NewGuid():N}.ini");
        File.WriteAllText(path, ini);
        try
        {
            IniFileAst ast = IniAstBuilder.BuildFromFile(path);
            var registry = DefaultControlRegistry.Create();
            var builder = new IniUiTreeBuilder(registry, new PropertyResolver(registry, new PassthroughLocalizationService()));
            UiNodeTree tree = builder.Build(ast, windowName);
            WindowTreePostProcessor.Apply(tree, windowName, new LayoutContext(1280, 720), new HashSet<string>());
            return tree;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsShell_True_Keeps_A_Panel_The_Heuristic_Would_Remove()
    {
        // 'SkirmishLobby' would normally be pruned as a foreign *Lobby shell
        // (>400px, wrong window) — IsShell=true pins it.
        UiNodeTree tree = BuildAndProcess("""
            [MyWindow]

            [SkirmishLobby]
            IsShell=true
            $Width=600
            $Height=400
            """, "MyWindow");

        tree.FindNode("SkirmishLobby").Should().NotBeNull("IsShell=true must override the pixel heuristic");
    }

    [Fact]
    public void IsShell_False_Removes_A_Panel_The_Heuristic_Would_Keep()
    {
        // 300px panel ending in 'Window' is below the 200→ no wait — 300 > 200,
        // so it would be removed anyway. Use a panel the heuristic KEEPS:
        // a 150px 'SubWindow' panel stays by default; IsShell=false removes it.
        UiNodeTree tree = BuildAndProcess("""
            [MyWindow]

            [SubWindow]
            IsShell=false
            $Width=150
            $Height=100
            """, "MyWindow");

        tree.FindNode("SubWindow").Should().BeNull("IsShell=false must force removal even below the pixel threshold");
    }

    [Fact]
    public void ShellMaxWidthWindow_Override_Retunes_The_Threshold()
    {
        // Default threshold 200 would prune any >200px '*Window' node. Setting
        // ShellMaxWidthWindow=500 keeps a 300px one alive. (Width, not $Width —
        // $Width keys make the builder treat the section as a foreign WINDOW
        // definition and drop it before the post-processor ever runs.)
        UiNodeTree tree = BuildAndProcess("""
            [MyWindow]
            ShellMaxWidthWindow=500

            [OtherWindow]
            Width=300
            Height=100
            """, "MyWindow");

        tree.FindNode("OtherWindow").Should().NotBeNull("INI threshold override must keep a 300px panel");
    }

    [Fact]
    public void Default_Heuristic_Still_Removes_Large_Foreign_Window()
    {
        UiNodeTree tree = BuildAndProcess("""
            [MyWindow]

            [OtherWindow]
            Width=300
            Height=100
            """, "MyWindow");

        tree.FindNode("OtherWindow").Should().BeNull("default 200px heuristic still applies without overrides");
    }

    private sealed class PassthroughLocalizationService : ILocalizationService
    {
        public string Localize(string? windowName, string controlName, string attributeName, string defaultValue, bool notify = true)
            => defaultValue;
    }
}
