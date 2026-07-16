using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace ClientAvalonia.Rendering;

/// <summary>
/// View-model for a single starting-location marker drawn on MapPreviewBox.
/// Coordinates are in MapPreviewBox control pixels (after letterbox projection).
/// </summary>
public sealed class MapStartMarkerVm : INotifyPropertyChanged
{
    private double _left;
    private double _top;
    private double _width = 32;
    private double _height = 32;
    private bool _isOccupied;
    private bool _isSelectable = true;
    private bool _isHovered;
    private string _label = string.Empty;
    private string _occupantText = string.Empty;
    private IBrush _fillBrush = Brushes.White;
    private IBrush _ringBrush = Brushes.Gray;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>1-based starting location index (matches DX PlayerInfo.StartingLocation).</summary>
    public int Index { get; init; }

    public double Left
    {
        get => _left;
        set => Set(ref _left, value);
    }

    public double Top
    {
        get => _top;
        set => Set(ref _top, value);
    }

    public double Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => Set(ref _height, value);
    }

    public bool IsOccupied
    {
        get => _isOccupied;
        set => Set(ref _isOccupied, value);
    }

    public bool IsSelectable
    {
        get => _isSelectable;
        set => Set(ref _isSelectable, value);
    }

    public bool IsHovered
    {
        get => _isHovered;
        set => Set(ref _isHovered, value);
    }

    /// <summary>Number glyph shown inside the ring (e.g. "1").</summary>
    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    /// <summary>Occupant name label(s) drawn beside the ring.</summary>
    public string OccupantText
    {
        get => _occupantText;
        set
        {
            if (Set(ref _occupantText, value))
                OnPropertyChanged(nameof(HasOccupantText));
        }
    }

    public bool HasOccupantText => !string.IsNullOrWhiteSpace(OccupantText);

    public IBrush FillBrush
    {
        get => _fillBrush;
        set => Set(ref _fillBrush, value);
    }

    public IBrush RingBrush
    {
        get => _ringBrush;
        set => Set(ref _ringBrush, value);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
