using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Code-driven Security / CnCNet WAF options panel (not present in stock MG OptionsWindow.ini).</summary>
internal static class OptionsSecurityControlsBootstrap
{
    public static void Apply(UiNodeTree tree)
    {
        UiNode? panel = tree.FindNode("SecurityOptionsPanel");
        if (panel == null)
            return;

        EnsureLabel(tree, panel, "lblWafIntro",
            "联机入网防护（WAF）：检查异常房间广播与可疑聊天/私信。默认仅警告，拦截需你确认加入屏蔽名单。私信来源策略（CnCNet 页）优先粗筛；选「所有人」时内容防护完全依赖本页 WAF。");

        EnsureCheckBox(tree, panel, "chkWafEnabled", "启用入网防护");
        EnsureCheckBox(tree, panel, "chkWafCheckProtocol", "检查协议流量（GAME / 隧道 / 频率）");
        EnsureCheckBox(tree, panel, "chkWafCheckListingText", "检查房间列表文案（房间名/地图/模式）");
        EnsureCheckBox(tree, panel, "chkWafCheckChannelChat", "检查频道聊天");
        EnsureCheckBox(tree, panel, "chkWafCheckPrivateChat", "检查私信内容");
        EnsureCheckBox(tree, panel, "chkWafAutoHideHighRisk", "自动隐藏高风险项（不直接拦截）");
        EnsureCheckBox(tree, panel, "chkWafAllowHeuristicDrop", "允许启发式拦截（高级，默认关）");

        EnsureLabel(tree, panel, "lblWafSensitivity", "灵敏度：");
        EnsureDropDown(tree, panel, "ddWafSensitivity", "低,中,高");

        EnsureButton(tree, panel, "btnWafStrategies", "策略预览与调整…", width: 220);

        EnsureLabel(tree, panel, "lblWafBlocklistCount",
            "屏蔽名单：空（命中告警时可「加入屏蔽名单」）");
        EnsureListBox(tree, panel, "lbWafBlocklist");
        EnsureLabel(tree, panel, "lblWafBlockKey", "添加屏蔽（昵称 / nick= / tunnel=ip:port / room=#chan）：");
        EnsureTextBox(tree, panel, "tbWafBlockKey", "例如：Alice 或 nick=Alice");
        EnsureLabel(tree, panel, "lblWafBlockNote", "备注（可选）：");
        EnsureTextBox(tree, panel, "tbWafBlockNote", "例如：私信推广");
        EnsureButton(tree, panel, "btnWafBlockAdd", "添加");
        EnsureButton(tree, panel, "btnWafBlockRemove", "移除选中");
        EnsureButton(tree, panel, "btnWafBlockClear", "清空名单");
    }

    private static void EnsureLabel(UiNodeTree tree, UiNode panel, string id, string text)
    {
        UiNode node = Ensure(tree, panel, id, "XNALabel", "DxLabel");
        node.Props["Text"] = text;
        node.Props["Width"] = 520.0;
        node.Props["Height"] = id.Equals("lblWafIntro", StringComparison.OrdinalIgnoreCase) ? 48.0 : 20.0;
    }

    private static void EnsureCheckBox(UiNodeTree tree, UiNode panel, string id, string text)
    {
        UiNode node = Ensure(tree, panel, id, "XNAClientCheckBox", "DxCheckBox");
        node.Props["Text"] = text;
        node.Props["Width"] = 520.0;
        node.Props["Height"] = 22.0;
    }

    private static void EnsureDropDown(UiNodeTree tree, UiNode panel, string id, string items)
    {
        UiNode node = Ensure(tree, panel, id, "XNAClientDropDown", "DxComboBox");
        node.Props["Items"] = items;
        node.Props["Width"] = 160.0;
        node.Props["Height"] = 24.0;
    }

    private static void EnsureListBox(UiNodeTree tree, UiNode panel, string id)
    {
        UiNode node = Ensure(tree, panel, id, "XNAListBox", "DxListBox");
        node.Props["Width"] = 520.0;
        node.Props["Height"] = 120.0;
    }

    private static void EnsureTextBox(UiNodeTree tree, UiNode panel, string id, string watermark)
    {
        UiNode node = Ensure(tree, panel, id, "XNATextBox", "DxTextBox");
        node.Props["Width"] = 520.0;
        node.Props["Height"] = 28.0;
        node.Props["Watermark"] = watermark;
        node.Props["Suggestion"] = watermark;
    }

    private static void EnsureButton(UiNodeTree tree, UiNode panel, string id, string text, double width = 120.0)
    {
        UiNode node = Ensure(tree, panel, id, "XNAClientButton", "DxButton");
        node.Props["Text"] = text;
        node.Props["Width"] = width;
        node.Props["Height"] = 24.0;
    }

    private static UiNode Ensure(UiNodeTree tree, UiNode panel, string id, string controlType, string templateKey)
    {
        UiNode? node = tree.FindNode(id);
        if (node == null)
        {
            node = new UiNode
            {
                Id = id,
                ControlType = controlType,
                TemplateKey = templateKey,
                WindowName = "OptionsWindow",
                Parent = panel,
            };
            panel.Children.Add(node);
            return node;
        }

        if (node.Parent != panel)
        {
            node.Parent?.Children.Remove(node);
            node.Parent = panel;
            if (!panel.Children.Contains(node))
                panel.Children.Add(node);
        }

        node.TemplateKey = templateKey;
        return node;
    }
}
