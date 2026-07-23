using ClientAvalonia.CnCNet.Waf;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>Load/edit WAF blocklist on Options → Security.</summary>
public static class WafBlocklistApplier
{
    public static void Apply(UiNodeViewModel? optionsRoot, ICnCNetIngressWaf? waf)
    {
        if (optionsRoot == null || waf == null)
            return;

        UiNodeViewModel? list = Find(optionsRoot, "lbWafBlocklist");
        if (list == null)
            return;

        IReadOnlyList<WafBlockEntry> entries = waf.ListBlockedEntries();
        list.SetListItems(entries.Select(e => e.DisplayLine).ToList());
        if (entries.Count > 0 && list.SelectedIndex < 0)
            list.SelectedIndex = 0;
        else if (entries.Count == 0)
            list.SelectedIndex = -1;

        Find(optionsRoot, "lblWafBlocklistCount")?.SetDisplayText(
            entries.Count == 0
                ? "屏蔽名单：空（命中告警时可「加入屏蔽名单」）"
                : $"屏蔽名单：{entries.Count} 条（显示 类型/目标 · nick!ident@host · 备注）");
    }

    public static void RemoveSelected(UiNodeViewModel? optionsRoot, ICnCNetIngressWaf? waf)
    {
        if (optionsRoot == null || waf == null)
            return;

        UiNodeViewModel? list = Find(optionsRoot, "lbWafBlocklist");
        if (list == null)
            return;

        int index = list.SelectedIndex;
        IReadOnlyList<WafBlockEntry> entries = waf.ListBlockedEntries();
        if (index < 0 || index >= entries.Count)
            return;

        waf.Unblock(entries[index].Key);
        Apply(optionsRoot, waf);
        if (list.ListItems.Count > 0)
            list.SelectedIndex = Math.Min(index, list.ListItems.Count - 1);
    }

    public static bool TryAddFromInput(UiNodeViewModel? optionsRoot, ICnCNetIngressWaf? waf, out string status)
    {
        status = string.Empty;
        if (optionsRoot == null || waf == null)
        {
            status = "WAF 未就绪";
            return false;
        }

        UiNodeViewModel? tbKey = Find(optionsRoot, "tbWafBlockKey");
        UiNodeViewModel? tbNote = Find(optionsRoot, "tbWafBlockNote");
        string raw = tbKey?.InputText?.Trim() ?? string.Empty;
        string note = tbNote?.InputText?.Trim() ?? string.Empty;
        string key = WafBlockEntry.NormalizeManualKey(raw);
        if (string.IsNullOrWhiteSpace(key))
        {
            status = "请输入屏蔽项（昵称 / nick= / tunnel= / room= / ident= / host=）";
            return false;
        }

        waf.Block(WafBlockEntry.FromKey(key, note: string.IsNullOrEmpty(note) ? "手动添加" : note));
        if (tbKey != null)
            tbKey.InputText = string.Empty;
        if (tbNote != null)
            tbNote.InputText = string.Empty;
        Apply(optionsRoot, waf);
        status = "已添加：" + key;
        return true;
    }

    public static void ClearAll(UiNodeViewModel? optionsRoot, ICnCNetIngressWaf? waf)
    {
        waf?.ClearBlocklist();
        Apply(optionsRoot, waf);
    }

    private static UiNodeViewModel? Find(UiNodeViewModel? root, string id)
    {
        if (root == null)
            return null;
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;
        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = Find(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
