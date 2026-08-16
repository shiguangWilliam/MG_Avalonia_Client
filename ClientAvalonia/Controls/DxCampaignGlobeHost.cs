using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClientAvalonia.Rendering;
using ClientCore.Settings;

namespace ClientAvalonia.Controls;

/// <summary>
/// Hosts the TacticalGlobeView inside the Tactical campaign overlay. Reads the visible
/// mission list from the campaign root view-model, maps each mission to a stable
/// (lat, lon) via hashing, and keeps globe selection in sync with lbCampaignList.
/// </summary>
public class DxCampaignGlobeHost : Panel
{
    private readonly TacticalGlobeView _globe = new();
    private UiNodeViewModel? _listVm;
    private bool _suppressSelectionSync;

    public static readonly StyledProperty<UiNodeViewModel?> OverlayRootProperty =
        AvaloniaProperty.Register<DxCampaignGlobeHost, UiNodeViewModel?>(nameof(OverlayRoot));

    public DxCampaignGlobeHost()
    {
        Children.Add(_globe);
        _globe.HorizontalAlignment = HorizontalAlignment.Center;
        _globe.VerticalAlignment = VerticalAlignment.Center;
        _globe.NodeClicked += OnGlobeNodeClicked;
    }

    public UiNodeViewModel? OverlayRoot
    {
        get => GetValue(OverlayRootProperty);
        set => SetValue(OverlayRootProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OverlayRootProperty)
            BindRoot(change.NewValue as UiNodeViewModel);
    }

    private void BindRoot(UiNodeViewModel? root)
    {
        if (_listVm != null)
            _listVm.PropertyChanged -= OnListVmPropertyChanged;

        _listVm = root != null ? FindVm(root, "lbCampaignList") : null;

        if (_listVm != null)
        {
            _listVm.PropertyChanged += OnListVmPropertyChanged;
            RefreshNodes();
            SyncFromList();
        }
        else
        {
            _globe.Nodes = new List<TacticalGlobeView.GlobeNode>();
        }
    }

    private void OnListVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UiNodeViewModel.CatalogListItems)
            || e.PropertyName == nameof(UiNodeViewModel.SelectedIndex))
        {
            RefreshNodes();
            SyncFromList();
        }
    }

    private void RefreshNodes()
    {
        if (_listVm == null)
            return;

        var nodes = new List<TacticalGlobeView.GlobeNode>();
        int index = 0;
        foreach (CatalogListItemViewModel item in _listVm.CatalogListItems)
        {
            if (item.IsHeader)
            {
                index++;
                continue;
            }

            // Real coordinates from Battle.ini (GlobeLatitude/GlobeLongitude) take
            // priority; missions without coordinates fall back to a deterministic
            // hash spread so the globe still shows every mission.
            (double lat, double lon) = item.HasGlobePosition
                ? (item.GlobeLatitude!.Value, item.GlobeLongitude!.Value)
                : HashToSphere(item.Text ?? $"m{index}");

            nodes.Add(new TacticalGlobeView.GlobeNode(
                item.Text ?? $"M-{index:000}",
                lat,
                lon,
                locked: !item.IsEnabled,
                side: string.Empty));
            index++;
        }

        _globe.Nodes = nodes;
    }

    private void SyncFromList()
    {
        if (_listVm == null)
            return;

        // Map list index → node index (headers skipped).
        int nodeIndex = -1;
        int listIndex = 0;
        foreach (CatalogListItemViewModel item in _listVm.CatalogListItems)
        {
            if (item.IsHeader)
            {
                listIndex++;
                continue;
            }

            nodeIndex++;
            if (listIndex == _listVm.SelectedIndex)
            {
                _globe.SelectedNodeIndex = nodeIndex;
                return;
            }

            listIndex++;
        }

        _globe.SelectedNodeIndex = -1;
    }

    private void OnGlobeNodeClicked(object? sender, int nodeIndex)
    {
        if (_listVm == null)
            return;

        _suppressSelectionSync = true;
        try
        {
            int listIndex = -1;
            int counter = -1;
            foreach (CatalogListItemViewModel item in _listVm.CatalogListItems)
            {
                if (item.IsHeader)
                    continue;

                counter++;
                if (counter == nodeIndex)
                {
                    listIndex = _listVm.CatalogListItems.IndexOf(item);
                    break;
                }
            }

            if (listIndex >= 0)
                _listVm.SelectedIndex = listIndex;
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    /// <summary>Stable (lat, lon) from mission label hash — deterministic across sessions.</summary>
    private static (double Lat, double Lon) HashToSphere(string label)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in label)
            {
                hash = (hash ^ c) * 16777619;
            }

            double lat = (hash % 1000) / 1000.0 * 140.0 - 70.0;
            double lon = ((hash >> 10) % 3600) / 3600.0 * 360.0 - 180.0;
            return (lat, lon);
        }
    }

    private static UiNodeViewModel? FindVm(UiNodeViewModel root, string id)
    {
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = FindVm(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
