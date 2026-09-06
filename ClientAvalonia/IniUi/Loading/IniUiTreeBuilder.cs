// IniUiTreeBuilder: converts an IniDocument into a UiNodeTree, replicating DX's
// implicit control-creation rules (R2-R8). Read ClientAvalonia/IniUi/README.md
// before changing ShouldSkipOrphanSection / InferControlType / TryInferKnownControlType —
// filters here directly affect MG/LNOD/QEC compatibility (ThreeModCompatibilityTests).
using ClientAvalonia.IniUi.Ast;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Schema;
using ClientAvalonia.Services;
using Rampastring.Tools;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>AST → UiNode tree with dynamic $CC / orphan section adoption.</summary>
public sealed class IniUiTreeBuilder
{
    private readonly ControlRegistry _registry;
    private readonly PropertyResolver _propertyResolver;

    public IniUiTreeBuilder(ControlRegistry registry, PropertyResolver propertyResolver)
    {
        _registry = registry;
        _propertyResolver = propertyResolver;
    }

    public UiNodeTree Build(IniFileAst ast, string windowSectionName)
    {
        // Control-driven model (aligned with ClientGUI/XNAWindow.cs + INItializableWindow.cs):
        //   1. Try [windowSectionName]          — modern INI convention.
        //   2. Try [GenericWindow]              — XNAWindow.GetINIAttributes() generic fallback.
        //   3. Synthesize an empty root section — control-section driven INIs (legacy QEC/YS)
        //      where the file only has [INISystem]/BasedOn + child control sections.
        IniDocument ini = ast.Document;
        IniSection? rootSection = ini.GetSection(windowSectionName)
            ?? ini.GetSection("GenericWindow");

        if (rootSection == null)
        {
            // No window-level attributes; the window is fully described by its child sections.
            rootSection = new IniSection { SectionName = windowSectionName };
        }

        var root = CreateNode(windowSectionName, "XNAPanel", windowSectionName);
        _propertyResolver.ApplySectionAttributes(root, rootSection, windowSectionName);
        ApplyMainMenuDefaults(root, windowSectionName);

        var tree = new UiNodeTree { Root = root, SourcePath = ast.SourcePath };

        ParseExtraControlsSection(ini, root, "$ExtraControls", windowSectionName, tree);
        ParseExtraControlsSection(ini, root, "ExtraControls", windowSectionName, tree);
        ParseBaseSectionChildren(ini, rootSection, root, windowSectionName, tree);
        ParseChildControlsFromSection(ini, rootSection, root, windowSectionName, tree);

        // R4/R6 alignment: apply attributes to declared children, then adopt any remaining
        // standalone control sections, then expand $CC references to a fixed point.
        //
        // Issue #17: the previous Attach→Adopt→Attach triple was order-sensitive — an
        // adopted panel's [$CC] reference only resolved if its target section appeared
        // earlier in the INI. Now adoption runs once and $CC expansion iterates until
        // no new nodes appear (bounded), so section order in the file no longer matters.
        AttachDeclaredSections(ini, root, windowSectionName, tree);
        AdoptOrphanControlSections(ini, root, windowSectionName, ast.OverlaySectionNames, tree);
        ExpandChildDeclarationsToFixedPoint(ini, root, windowSectionName, tree);
        ParsePanelExtraControls(ini, tree, windowSectionName);

        // Issue #16: surface collected per-node diagnostics — window stays usable,
        // modders get exact section/definition/reason lines in client.log.
        if (tree.Diagnostics.Count > 0)
        {
            Logger.Log(
                $"IniUiTreeBuilder: {tree.Diagnostics.Count} control definition(s) skipped in '{windowSectionName}' ({ast.SourcePath}):");
            foreach (string diagnostic in tree.Diagnostics)
                Logger.Log($"  - {diagnostic}");
        }

        return tree;
    }

    /// <summary>
    /// Issue #17: iteratively expands $CC declarations on every known node until a
    /// fixed point. Each round sees panels adopted in previous rounds, so a $CC
    /// reference resolves regardless of where its section sits in the INI file.
    /// The iteration bound guards against pathological self-referencing cycles.
    /// </summary>
    private void ExpandChildDeclarationsToFixedPoint(IniDocument ini, UiNode root, string windowName, UiNodeTree tree)
    {
        const int maxRounds = 16;
        for (int round = 0; round < maxRounds; round++)
        {
            int nodesBefore = CountNodes(root);
            AttachDeclaredSections(ini, root, windowName, tree);
            if (CountNodes(root) == nodesBefore)
                return;
        }

        tree.Diagnostics.Add(
            $"$CC expansion hit the {maxRounds}-round fixed-point bound — check for cyclic child references.");
    }

    private static int CountNodes(UiNode node)
    {
        int count = 1;
        foreach (UiNode child in node.Children)
            count += CountNodes(child);
        return count;
    }

    private static void ApplyMainMenuDefaults(UiNode root, string windowSectionName)
    {
        if (!windowSectionName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
            return;

        if (!root.Props.ContainsKey("Background"))
            root.Props["Background"] = "MainMenu/mainmenubg.png";
    }

    private void ParseBaseSectionChildren(
        IniDocument ini,
        IniSection rootSection,
        UiNode root,
        string windowName,
        UiNodeTree tree)
    {
        string baseSectionName = rootSection.GetStringValue("$BaseSection", string.Empty);
        if (string.IsNullOrWhiteSpace(baseSectionName))
            return;

        IniSection? baseSection = ini.GetSection(baseSectionName);
        if (baseSection == null)
            return;

        ParseChildControlsFromSection(ini, baseSection, root, windowName, tree);
    }

    private void ParsePanelExtraControls(IniDocument ini, UiNodeTree tree, string windowName)
    {
        foreach (IniSection section in ini.Sections)
        {
            if (!section.SectionName.EndsWith("ExtraControls", StringComparison.OrdinalIgnoreCase))
                continue;

            string panelName = section.SectionName[..^"ExtraControls".Length];
            UiNode? panel = tree.FindNode(panelName);
            if (panel == null)
                continue;

            foreach (KeyValuePair<string, string> kvp in section.Keys)
            {
                if (kvp.Key.StartsWith(';') || kvp.Key.StartsWith('#'))
                    continue;

                UiNode? child = TryAddChildFromDefinition(panel, kvp.Value, windowName, tree, section.SectionName);
                if (child == null)
                    continue;

                IniSection? childSection = ini.GetSection(child.Id);
                if (childSection != null)
                {
                    _propertyResolver.ApplySectionAttributes(child, childSection, windowName);
                    ParseChildControlsFromSection(ini, childSection, child, windowName, tree);
                }
            }
        }
    }

    private void AdoptOrphanControlSections(
        IniDocument ini,
        UiNode root,
        string windowName,
        IReadOnlySet<string> overlaySections,
        UiNodeTree tree)
    {
        foreach (IniSection section in ini.Sections)
        {
            if (ShouldSkipOrphanSection(section, windowName, overlaySections))
                continue;

            if (tree.FindNode(section.SectionName) != null)
                continue;

            // DX semantics (XNAWindowBase.ReadChildControlAttributes + INItializableWindow.ReadINIForControl):
            // any section that is not an INI-level meta section is treated as a control's config block.
            // We adopt by name; the type is inferred. Then we recurse into $CC children it declares
            // and let AttachDeclaredSections apply attributes on the next pass.
            string inferredType = InferControlType(section);
            var node = CreateNode(section.SectionName, inferredType, windowName);
            node.Parent = root;
            root.Children.Add(node);
            _propertyResolver.ApplySectionAttributes(node, section, windowName);

            // R4 alignment: a section may declare its own children via $CC keys.
            // DX INItializableWindow.ReadINIForControl recurses into them — we must too,
            // otherwise panel-internal $CC children (e.g. [GameOptionsPanel] $CC_00=cmbTSFS:...)
            // never get materialized and the whole UI group silently disappears.
            ParseChildControlsFromSection(ini, section, node, windowName, tree);
        }
    }

    private static bool ShouldSkipOrphanSection(IniSection section, string windowName, IReadOnlySet<string> overlaySections)
    {
        string name = section.SectionName;

        // INI-level meta sections (DX never treats these as controls).
        if (name is "INISystem" or "$ExtraControls" or "ExtraControls" or "MainMenuUIPanel")
            return true;

        // The active window itself is the root, not a child.
        if (name.Equals(windowName, StringComparison.OrdinalIgnoreCase))
            return true;

        // GenericWindow is a shared style template (DX XNAWindow), never a child.
        if (name is "GameLobbyBase" or "GenericWindow")
            return true;

        // *ExtraControls sections are scanned separately by ParsePanelExtraControls.
        if (name.EndsWith("ExtraControls", StringComparison.OrdinalIgnoreCase))
            return true;

        // R6 alignment: sections that look like ANOTHER window definition (DX XNAWindowBase
        // treats them as separate windows). Match DX's SectionLooksLikeForeignWindow but
        // require strong signals — a bare panel named "...Window" is NOT a foreign window
        // unless it also declares window-level attributes (DrawMode/Size/$Width).
        if (SectionLooksLikeForeignWindow(name, windowName, section))
            return true;

        // DX XNAWindow / CampaignSelector only styles existing children (or ExtraControls/$CC).
        // It never invents controls from orphan BasedOn sections. Avalonia must adopt orphans
        // for INI-driven lobbies (QEC SkirmishLobby → MultiplayerGameLobby), but for DX
        // code-built overlays like CampaignSelector, BasedOn-only leftovers such as
        // GenericWindow.ini's [chkPersistentMode] must not become real UI.
        if (RestrictOrphansToOverlayFile(windowName)
            && overlaySections.Count > 0
            && !overlaySections.Contains(name))
            return true;

        // NOTE: Do NOT globally restrict adoption to overlay sections — that breaks QEC
        // BasedOn chains where btnLaunchGame lives in MultiplayerGameLobby.ini.
        return false;
    }

    /// <summary>
    /// Windows that DX builds in C# (then themes via INI). Orphan adoption should only
    /// materialize sections declared in the window's own INI file.
    /// </summary>
    private static bool RestrictOrphansToOverlayFile(string windowName)
        => FloatingOverlayLayout.IsCampaignWindow(windowName)
           || windowName.Equals("PrivacyNotification", StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("UpdateQueryWindow", StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("ManualUpdateQueryWindow", StringComparison.OrdinalIgnoreCase);

    private static bool SectionLooksLikeForeignWindow(string sectionName, string activeWindow, IniSection section)
    {
        if (sectionName.Equals(activeWindow, StringComparison.OrdinalIgnoreCase))
            return false;

        bool looksLikeWindow = sectionName.EndsWith("Window", StringComparison.OrdinalIgnoreCase)
            || sectionName.EndsWith("Lobby", StringComparison.OrdinalIgnoreCase);

        if (!looksLikeWindow)
            return false;

        return section.KeyExists("DrawMode") || section.KeyExists("$Width") || section.KeyExists("Size");
    }

    private static string InferControlType(IniSection section)
    {
        string id = section.SectionName;

        // 1) Explicit name-based matches first — these are typed subclasses in DX and must
        //    take priority over prefix heuristics (e.g. btnLaunchGame is GameLaunchButton,
        //    NOT XNAClientButton).
        string? known = TryInferKnownControlType(id, section);
        if (known != null)
            return known;

        // 2) Game-option signals (SpawnIni / CustomIni / MapCode) — must be checked before
        //    generic prefix matching so cmb/chk get the GameLobby* subtype.
        bool isGameOption = IsGameLobbyOptionSection(section);

        // 3) Prefix-based heuristics aligned to DX $CC type table (GameLobbyBase.ini /
        //    MultiplayerGameLobby.ini / GenericWindow.ini).
        if (id.StartsWith("chk", StringComparison.OrdinalIgnoreCase))
        {
            if (section.KeyExists("SettingSection") || section.KeyExists("SettingKey") || section.KeyExists("DefaultValue"))
                return "SettingCheckBox";
            return isGameOption ? "GameLobbyCheckBox" : "XNAClientCheckBox";
        }

        if (id.StartsWith("cmb", StringComparison.OrdinalIgnoreCase))
            return isGameOption ? "GameLobbyDropDown" : "XNAClientDropDown";

        if (id.StartsWith("dd", StringComparison.OrdinalIgnoreCase)
            || section.KeyExists("Items"))
        {
            if (section.KeyExists("SettingSection") || section.KeyExists("SettingKey"))
                return "SettingDropDown";
            return isGameOption ? "GameLobbyDropDown" : "XNAClientDropDown";
        }

        if (id.StartsWith("btn", StringComparison.OrdinalIgnoreCase))
            return section.KeyExists("URL") ? "XNALinkButton" : "XNAClientButton";

        if (id.StartsWith("trb", StringComparison.OrdinalIgnoreCase))
            return "XNATrackbar";

        // 4) Attribute-based heuristics for sections whose names don't follow the prefix
        //    convention (winbar_*, glow_*, arbitrary panel names).
        if (section.KeyExists("IdleTexture") || section.KeyExists("HoverTexture"))
            return section.KeyExists("URL") ? "XNALinkButton" : "XNAClientButton";

        if (section.KeyExists("Suggestion"))
            return "XNASuggestionTextBox";

        if (section.KeyExists("BackgroundTexture") || section.KeyExists("SolidColorBackgroundTexture"))
            return "XNAPanel";

        if (section.KeyExists("Text"))
            return section.KeyExists("IdleColor") && section.KeyExists("HoverColor")
                ? "XNALinkLabel"
                : "XNALabel";

        // 5) Last-resort: any layout-only section is a panel. This mirrors DX's behavior
        //    where a section with only Location/X/Y/Width/Height and no widget-specific
        //    keys is treated as a plain XNAPanel container.
        return "XNAPanel";
    }

    private void AttachDeclaredSections(IniDocument ini, UiNode root, string windowName, UiNodeTree tree)
    {
        foreach (UiNode node in EnumerateNodes(root))
        {
            IniSection? section = ini.GetSection(node.Id);
            if (section == null)
                continue;

            _propertyResolver.ApplySectionAttributes(node, section, windowName);
            ParseChildControlsFromSection(ini, section, node, windowName, tree);
        }
    }

    private void ParseExtraControlsSection(IniDocument ini, UiNode parent, string sectionName, string windowName, UiNodeTree tree)
    {
        IniSection? section = ini.GetSection(sectionName);
        if (section == null)
            return;

        foreach (KeyValuePair<string, string> kvp in section.Keys)
        {
            if (sectionName == "$ExtraControls" && !kvp.Key.StartsWith("$CC", StringComparison.Ordinal))
                continue;

            TryAddChildFromDefinition(parent, kvp.Value, windowName, tree, sectionName);
        }
    }

    private void ParseChildControlsFromSection(
        IniDocument ini,
        IniSection section,
        UiNode parent,
        string windowName,
        UiNodeTree tree)
    {
        foreach (KeyValuePair<string, string> kvp in section.Keys)
        {
            if (!kvp.Key.StartsWith("$CC", StringComparison.Ordinal))
                continue;

            UiNode? child = TryAddChildFromDefinition(parent, kvp.Value, windowName, tree, section.SectionName);
            if (child == null)
                continue;

            IniSection? childSection = ini.GetSection(child.Id);
            if (childSection != null)
            {
                _propertyResolver.ApplySectionAttributes(child, childSection, windowName);
                ParseChildControlsFromSection(ini, childSection, child, windowName, tree);
            }
        }
    }

    /// <summary>
    /// Issue #16: malformed definitions no longer throw (which killed the whole
    /// window). Returns null after recording a diagnostic; the caller skips the
    /// child and keeps building the rest of the tree.
    /// </summary>
    private UiNode? TryAddChildFromDefinition(UiNode parent, string definition, string windowName, UiNodeTree tree, string sourceSection)
    {
        string[] parts = definition.Split(':');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            tree.Diagnostics.Add(
                $"[{sourceSection}] invalid child control definition '{definition}' — expected '<id>:<type>', child skipped.");
            return null;
        }

        string childName = parts[0].Trim();
        string typeName = parts[1].Trim();

        UiNode? existing = tree.FindNode(childName);
        if (existing != null)
        {
            if (existing.Parent != null)
                existing.Parent.Children.Remove(existing);

            existing.Parent = parent;
            if (!parent.Children.Contains(existing))
                parent.Children.Add(existing);

            return existing;
        }

        UiNode? sibling = parent.Children.FirstOrDefault(c => c.Id.Equals(childName, StringComparison.OrdinalIgnoreCase));
        if (sibling != null)
            return sibling;

        ControlTypeDefinition typeDef;
        try
        {
            typeDef = _registry.Resolve(typeName);
        }
        catch (Exception ex)
        {
            tree.Diagnostics.Add(
                $"[{sourceSection}] unknown control type '{typeName}' for '{childName}' ({ex.Message}) — child skipped.");
            return null;
        }

        var child = CreateNode(childName, typeDef.IniTypeName, windowName, typeDef.TemplateKey);
        child.Parent = parent;
        parent.Children.Add(child);
        return child;
    }

    private UiNode AddChildFromDefinition(UiNode parent, string definition, string windowName, UiNodeTree tree)
        => TryAddChildFromDefinition(parent, definition, windowName, tree, "$CC") ?? throw new InvalidOperationException($"Invalid child control definition: {definition}");

    /// <summary>
    /// SpawnIni / MapCode / CustomIni game-option controls (DX GameLobbyCheckBox/DropDown).
    /// </summary>
    private static bool IsGameLobbyOptionSection(IniSection section)
        => section.KeyExists("SpawnIniOption")
           || section.KeyExists("CustomIniPath")
           || section.KeyExists("OptionName")
           || section.KeyExists("DataWriteMode");

    private static string? TryInferKnownControlType(string id, IniSection section)
    {
        string lower = id.ToLowerInvariant();

        // 1) Exact-name matches — typed subclasses in DX (GameLobbyBase.cs code-behind or special
        //    $CC declarations). These MUST take priority over prefix heuristics because they have
        //    runtime behavior beyond what the prefix suggests (e.g. btnLaunchGame is GameLaunchButton,
        //    NOT XNAClientButton).
        switch (lower)
        {
            case "btnlaunchgame":
                return "GameLaunchButton";
            case "lbmaplist":
                return "XNAMultiColumnListBox";
            case "lbcampaignlist":
                return "XNAListBox";
            case "lblcurrentchannel":
            case "lblcolor":
                return "XNALabel";
            case "ddcurrentchannel":
            case "ddcolor":
                return "XNAClientDropDown";
            case "mappreviewbox":
                return "MapPreviewBox";
        }

        // 2) Prefix-with-suffix matches — DX code creates multiple variants per lobby
        //    (e.g. ChatListBox lbChatMessages_Host / lbChatMessages_Player). The _Host / _Player
        //    suffix does not change the type. Handle this here, before the generic prefix table,
        //    so that lb* variants resolve to ChatListBox, not XNAPanel.
        if (lower.StartsWith("lbchatmessages", StringComparison.Ordinal))
            return "ChatListBox";
        if (lower.StartsWith("lbgamelist", StringComparison.Ordinal) || lower.StartsWith("lbplayerlist", StringComparison.Ordinal))
            return "ChatListBox";

        if (lower.StartsWith("tbchatinput", StringComparison.Ordinal))
            return "XNAChatTextBox";
        if (lower.StartsWith("tbmapsearch", StringComparison.Ordinal) || lower.StartsWith("tbgamesearch", StringComparison.Ordinal))
            return "XNASuggestionTextBox";

        // 3) Generic prefix table — aligned to DX $CC type table conventions. Any control whose
        //    section name starts with one of these prefixes inherits the corresponding type.
        //    This is essential for QEC/YS-style INIs that don't use $CC declarations and rely
        //    entirely on section-name conventions (lblFoo, lbBar, tbQuux, ...).
        if (lower.StartsWith("lbl", StringComparison.Ordinal))
            return "XNALabel";
        if (lower.StartsWith("lb", StringComparison.Ordinal))
            return "XNAListBox";
        if (lower.StartsWith("tb", StringComparison.Ordinal))
            return "XNATextBox";
        if (lower.StartsWith("btn", StringComparison.Ordinal))
            return section.KeyExists("URL") ? "XNALinkButton" : "XNAClientButton";
        if (lower.StartsWith("dd", StringComparison.Ordinal))
            return "XNAClientDropDown";
        if (lower.StartsWith("cmb", StringComparison.Ordinal))
            return IsGameLobbyOptionSection(section) ? "GameLobbyDropDown" : "XNAClientDropDown";
        if (lower.StartsWith("chk", StringComparison.Ordinal))
        {
            if (section.KeyExists("SettingSection") || section.KeyExists("SettingKey") || section.KeyExists("DefaultValue"))
                return "SettingCheckBox";
            return IsGameLobbyOptionSection(section) ? "GameLobbyCheckBox" : "XNAClientCheckBox";
        }
        if (lower.StartsWith("trb", StringComparison.Ordinal))
            return "XNATrackbar";

        return null;
    }

    private UiNode CreateNode(string id, string controlType, string windowName, string? templateKey = null)
    {
        ControlTypeDefinition def = _registry.Resolve(controlType);
        return new UiNode
        {
            Id = id,
            ControlType = controlType,
            TemplateKey = templateKey ?? def.TemplateKey,
            WindowName = windowName,
        };
    }

    private static IEnumerable<UiNode> EnumerateNodes(UiNode node)
    {
        yield return node;
        foreach (UiNode child in node.Children)
        {
            foreach (UiNode n in EnumerateNodes(child))
                yield return n;
        }
    }
}
