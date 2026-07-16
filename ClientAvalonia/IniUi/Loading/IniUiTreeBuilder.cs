using ClientAvalonia.IniUi.Ast;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Schema;

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
        IniDocument ini = ast.Document;
        IniSection? rootSection = ini.GetSection(windowSectionName)
            ?? throw new InvalidOperationException($"Section [{windowSectionName}] not found in {ast.SourcePath}");

        var root = CreateNode(windowSectionName, "XNAPanel", windowSectionName);
        _propertyResolver.ApplySectionAttributes(root, rootSection, windowSectionName);
        ApplyMainMenuDefaults(root, windowSectionName);

        var tree = new UiNodeTree { Root = root, SourcePath = ast.SourcePath };

        ParseExtraControlsSection(ini, root, "$ExtraControls", windowSectionName, tree);
        ParseExtraControlsSection(ini, root, "ExtraControls", windowSectionName, tree);
        ParseBaseSectionChildren(ini, rootSection, root, windowSectionName, tree);
        ParseChildControlsFromSection(ini, rootSection, root, windowSectionName, tree);

        AttachDeclaredSections(ini, root, windowSectionName, tree);
        ParsePanelExtraControls(ini, tree, windowSectionName);
        AdoptOrphanControlSections(ini, root, windowSectionName, ast.OverlaySectionNames, tree);

        return tree;
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

                UiNode child = AddChildFromDefinition(panel, kvp.Value, windowName, tree);
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

            if (!SectionLooksLikeControl(section))
                continue;

            string inferredType = InferControlType(section);
            var node = CreateNode(section.SectionName, inferredType, windowName);
            node.Parent = root;
            root.Children.Add(node);
            _propertyResolver.ApplySectionAttributes(node, section, windowName);
        }
    }

    private static bool ShouldSkipOrphanSection(IniSection section, string windowName, IReadOnlySet<string> overlaySections)
    {
        string name = section.SectionName;

        if (name is "INISystem" or "$ExtraControls" or "ExtraControls" or "MainMenuUIPanel")
            return true;

        if (name.Equals(windowName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.EndsWith("ExtraControls", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name is "GameLobbyBase" or "GenericWindow")
            return true;

        if (SectionLooksLikeForeignWindow(name, windowName, section))
            return true;

        // For overlay INI files, only adopt sections declared in the overlay (modder controls).
        if (overlaySections.Count > 0 && !overlaySections.Contains(name))
            return true;

        return false;
    }

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

        string? known = TryInferKnownControlType(id, section);
        if (known != null)
            return known;

        if (id.StartsWith("chk", StringComparison.OrdinalIgnoreCase))
        {
            if (section.KeyExists("SettingSection") || section.KeyExists("SettingKey") || section.KeyExists("DefaultValue"))
                return "SettingCheckBox";
            return IsGameLobbyOptionSection(section) ? "GameLobbyCheckBox" : "XNAClientCheckBox";
        }

        if (id.StartsWith("dd", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("cmb", StringComparison.OrdinalIgnoreCase)
            || section.KeyExists("Items"))
        {
            if (section.KeyExists("SettingSection") || section.KeyExists("SettingKey"))
                return "SettingDropDown";
            return IsGameLobbyOptionSection(section) ? "GameLobbyDropDown" : "XNAClientDropDown";
        }

        if (section.KeyExists("Checked") || IsGameLobbyOptionSection(section))
            return "GameLobbyCheckBox";

        if (id.StartsWith("btn", StringComparison.OrdinalIgnoreCase))
            return section.KeyExists("URL") ? "XNALinkButton" : "XNAClientButton";

        if (id.StartsWith("trb", StringComparison.OrdinalIgnoreCase))
            return "XNATrackbar";

        if (section.KeyExists("IdleTexture") || section.KeyExists("HoverTexture"))
            return section.KeyExists("URL") ? "XNALinkButton" : "XNAClientButton";

        if (section.KeyExists("Suggestion"))
            return "XNASuggestionTextBox";

        if (section.KeyExists("BackgroundTexture") || section.KeyExists("SolidColorBackgroundTexture"))
            return "XNAExtraPanel";

        if (section.KeyExists("Text"))
            return section.KeyExists("IdleColor") && section.KeyExists("HoverColor")
                ? "XNALinkLabel"
                : "XNALabel";

        if (section.KeyExists("Location") || section.KeyExists("$X") || section.KeyExists("X"))
            return "XNALabel";

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

            AddChildFromDefinition(parent, kvp.Value, windowName, tree);
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

            UiNode child = AddChildFromDefinition(parent, kvp.Value, windowName, tree);

            IniSection? childSection = ini.GetSection(child.Id);
            if (childSection != null)
            {
                _propertyResolver.ApplySectionAttributes(child, childSection, windowName);
                ParseChildControlsFromSection(ini, childSection, child, windowName, tree);
            }
        }
    }

    private UiNode AddChildFromDefinition(UiNode parent, string definition, string windowName, UiNodeTree tree)
    {
        string[] parts = definition.Split(':');
        if (parts.Length != 2)
            throw new InvalidOperationException($"Invalid child control definition: {definition}");

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

        ControlTypeDefinition typeDef = _registry.Resolve(typeName);
        var child = CreateNode(childName, typeDef.IniTypeName, windowName, typeDef.TemplateKey);
        child.Parent = parent;
        parent.Children.Add(child);
        return child;
    }

    private static bool SectionLooksLikeControl(IniSection section)
    {
        if (IsKnownHardcodedControlSection(section.SectionName))
            return true;

        return section.Keys.Any(k => k.Key is "IdleTexture" or "BackgroundTexture" or "Text" or "$X" or "Location" or "Checked" or "Items"
            or "DistanceFromRightBorder" or "DistanceFromBottomBorder" or "FillWidth" or "FillHeight" or "Enabled" or "Visible");
    }

    /// <summary>
    /// SpawnIni / MapCode / CustomIni game-option controls (DX GameLobbyCheckBox/DropDown).
    /// </summary>
    private static bool IsGameLobbyOptionSection(IniSection section)
        => section.KeyExists("SpawnIniOption")
           || section.KeyExists("CustomIniPath")
           || section.KeyExists("OptionName")
           || section.KeyExists("DataWriteMode");

    /// <summary>
    /// Controls created in XNA code (CnCNetLobby.cs) with layout-only INI sections — must be adopted as real widgets.
    /// </summary>
    private static bool IsKnownHardcodedControlSection(string id)
    {
        if (id.StartsWith("btn", StringComparison.OrdinalIgnoreCase))
            return true;

        if (id.StartsWith("dd", StringComparison.OrdinalIgnoreCase))
            return true;

        return id.Equals("tbChatInput", StringComparison.OrdinalIgnoreCase)
            || id.Equals("tbGameSearch", StringComparison.OrdinalIgnoreCase)
            || id.Equals("lblCurrentChannel", StringComparison.OrdinalIgnoreCase)
            || id.Equals("lbPlayerList", StringComparison.OrdinalIgnoreCase)
            || id.Equals("lbGameList", StringComparison.OrdinalIgnoreCase)
            || id.Equals("lbChatMessages", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryInferKnownControlType(string id, IniSection section)
    {
        switch (id.ToLowerInvariant())
        {
            case "lbplayerlist":
            case "lbgamelist":
            case "lbchatmessages":
                return "ChatListBox";
            case "lbmaplist":
                return "XNAMultiColumnListBox";
            case "lbcampaignlist":
                return "XNAListBox";
            case "tbchatinput":
                return "XNAChatTextBox";
            case "tbgamesearch":
            case "tbmapsearch":
                return "XNASuggestionTextBox";
            case "lblcurrentchannel":
            case "lblcolor":
                return "XNALabel";
            case "ddcurrentchannel":
            case "ddcolor":
                return "XNAClientDropDown";
        }

        if (id.StartsWith("btn", StringComparison.OrdinalIgnoreCase))
            return section.KeyExists("URL") ? "XNALinkButton" : "XNAClientButton";

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
