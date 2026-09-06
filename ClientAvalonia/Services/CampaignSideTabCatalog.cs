using System;
using System.Collections.Generic;
using System.Linq;
using ClientCore;
using ClientCore.Extensions;

namespace ClientAvalonia.Services;

/// <summary>
/// Issue #22: the campaign side tabs (GDI / Nod / ThirdSide / FourthSide …)
/// used to be hard-coded in three places (tab labels, tab-selected state,
/// click behaviors). This catalog derives the tab set from
/// GameOptions.ini [General] Sides= — the same source the skirmish lobby
/// dropdown uses — so a mod adding or reordering sides needs no client
/// rebuild for the campaign selector.
///
/// Mapping rule (mirrors DX Battle.ini side names): tab i drives the i-th
/// side; control ids follow the DX naming GDI/Nod/ThirdSide/FourthSide for
/// the first four and Side{i} beyond that.
/// </summary>
public static class CampaignSideTabCatalog
{
    public sealed record CampaignSideTab(string ControlId, string SideName, string DisplayNameFallback, string L10NKey);

    // DX CampaignSelector.ini control ids — the first four tabs are fixed by
    // the shipped INI; later sides fall back to Side{i}.
    private static readonly string[] KnownTabIds = ["GDI", "Nod", "ThirdSide", "FourthSide"];

    private static IReadOnlyList<CampaignSideTab>? _cached;

    public static void InvalidateCache() => _cached = null;

    public static IReadOnlyList<CampaignSideTab> GetTabs()
    {
        if (_cached != null)
            return _cached;

        var tabs = new List<CampaignSideTab>();
        try
        {
            string[] sides = ClientConfiguration.Instance.Sides
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            for (int i = 0; i < sides.Length; i++)
            {
                string sideName = sides[i];
                string controlId = i < KnownTabIds.Length ? KnownTabIds[i] : $"Side{i}";
                (string fallback, string key) = DisplayNameFor(sideName, i);
                tabs.Add(new CampaignSideTab(controlId, sideName, fallback, key));
            }
        }
        catch (InvalidOperationException)
        {
            // ClientConfiguration not initialized (unit tests) — fall back to
            // the classic trio so UI code always has a usable tab set.
            tabs.Add(new CampaignSideTab("GDI", "Allied", "同盟国联军", "Client:Main:SideAllied"));
            tabs.Add(new CampaignSideTab("Nod", "Soviet", "苏维埃联盟", "Client:Main:SideSoviet"));
            tabs.Add(new CampaignSideTab("ThirdSide", "Ackville", "阿克维尔", "Client:Main:SideAckville"));
        }

        _cached = tabs;
        return tabs;
    }

    /// <summary>Tab control id for the given campaign filter index (0-based side order).</summary>
    public static string ControlIdForIndex(int sideIndex)
    {
        IReadOnlyList<CampaignSideTab> tabs = GetTabs();
        return sideIndex >= 0 && sideIndex < tabs.Count
            ? tabs[sideIndex].ControlId
            : $"Side{sideIndex}";
    }

    private static (string Fallback, string Key) DisplayNameFor(string sideName, int index)
        => sideName.ToLowerInvariant() switch
        {
            "allied" => ("同盟国联军", "Client:Main:SideAllied"),
            "soviet" => ("苏维埃联盟", "Client:Main:SideSoviet"),
            "ackville" => ("阿克维尔", "Client:Main:SideAckville"),
            _ => (sideName, $"Client:Main:Side{index}"),
        };
}
