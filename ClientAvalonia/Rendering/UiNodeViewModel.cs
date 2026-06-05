using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Services;

namespace ClientAvalonia.Rendering;

public sealed class UiNodeViewModel : INotifyPropertyChanged
{
    private readonly ResourceResolver _resources;
    private readonly BehaviorRegistry _behaviors;
    private bool _isChecked;
    private bool _isEnabled = true;
    private int _selectedIndex;
    private string? _displayTextOverride;
    private string _inputText = string.Empty;

    public UiNodeViewModel(
        UiNode node,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        IEnumerable<UiNodeViewModel>? children = null)
    {
        Node = node;
        _resources = resources;
        _behaviors = behaviors;
        Children = new ObservableCollection<UiNodeViewModel>(children ?? []);
        ClickCommand = behaviors.CreateClickCommand(this);
        ComboItems = BuildComboItems();
        _isChecked = ReadInitialIsChecked();
        _isEnabled = ReadInitialIsEnabled();
        _selectedIndex = ReadInitialSelectedIndex();
        LoadImages();
    }

    public UiNode Node { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id => Node.Id;
    public string ControlType => Node.ControlType;
    public string TemplateKey => Node.TemplateKey;

    public ObservableCollection<UiNodeViewModel> Children { get; }

    public ObservableCollection<string> ComboItems { get; }

    public ObservableCollection<ComboItemViewModel> ComboItemEntries { get; } = [];

    public bool UseComboItemIcons { get; private set; }

    public ObservableCollection<string> ListItems { get; } = [];

    public ObservableCollection<CatalogListItemViewModel> CatalogListItems { get; } = [];

    public bool UseCatalogListItems { get; private set; }

    public event Action? SelectionChanged;

    public event Action? InputTextChanged;

    public ICommand ClickCommand { get; }

    public double CanvasLeft => Node.GetNumericProp("CanvasLeft");
    public double CanvasTop => Node.GetNumericProp("CanvasTop");
    public double Width => Node.GetNumericProp("Width");
    public double Height => Node.GetNumericProp("Height");
    public double ScrollContentHeight
    {
        get
        {
            if (Node.Props.TryGetValue("ScrollContentHeight", out object? v) && v is double d && d > 0)
                return d;
            return Math.Max(Height, 1);
        }
    }

    /// <summary>Canvas host height: expanded for scrollable option panels.</summary>
    public double LayoutCanvasHeight
        => TemplateKey == "DxOptionsScrollPanel" ? ScrollContentHeight : Height;
    public double ContentMaxWidth
    {
        get
        {
            double w = Width;
            if (w <= 0)
                return 520;

            return Math.Max(w, 120);
        }
    }
    public int ZIndex => Node.Props.TryGetValue("ZIndex", out object? z) && z is int i ? i : 0;

    public bool IsVisible
    {
        get => !Node.Props.TryGetValue("IsVisible", out object? v) || v is not bool b || b;
        set
        {
            Node.Props["IsVisible"] = value;
            OnPropertyChanged();
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            Node.Props["IsEnabled"] = value;
            OnPropertyChanged();
        }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;

            _isChecked = value;
            Node.Props["IsChecked"] = value;
            OnPropertyChanged();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
                return;

            _selectedIndex = value;
            Node.Props["SelectedIndex"] = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke();
        }
    }

    public string? Text => _displayTextOverride ?? GetString("Text");
    public string? Watermark => GetString("Watermark") ?? GetString("Suggestion");

    public string InputText
    {
        get => _inputText;
        set
        {
            if (_inputText == value)
                return;

            _inputText = value;
            OnPropertyChanged();
            InputTextChanged?.Invoke();
        }
    }

    public string? ToolTip => GetString("ToolTip");

    public bool IsTabSelected
        => Node.Props.TryGetValue("IsTabSelected", out object? v) && v is bool b && b;

    public bool ShowButtonText => !string.IsNullOrWhiteSpace(Text);

    public string ButtonLabel => string.IsNullOrWhiteSpace(Text) ? Id : Text!;

    /// <summary>Maps XNA FontIndex to Avalonia font size (MG buttons use FontIndex=1).</summary>
    public double FontSize
    {
        get
        {
            if (Node.Props.TryGetValue("FontSize", out object? fs) && fs is int explicitSize)
                return explicitSize;

            if (Node.Props.TryGetValue("FontIndex", out object? fi) && fi is int index)
            {
                return index switch
                {
                    0 => 11,
                    1 => 12.5,
                    _ => 11 + index,
                };
            }

            return 12;
        }
    }

    public Bitmap? IdleImage { get; private set; }
    public Bitmap? HoverImage { get; private set; }
    public Bitmap? BackgroundImage { get; private set; }
    public Bitmap? CheckBoxClearImage { get; private set; }
    public Bitmap? CheckBoxCheckedImage { get; private set; }
    public Bitmap? PreviewImage { get; private set; }

    public Bitmap? SideIcon { get; private set; }

    public Bitmap? ThumbImage { get; private set; }

    public bool HasThumbImage => ThumbImage != null;

    public IBrush? BackgroundBrush
    {
        get
        {
            if (Node.Props.TryGetValue("SolidColorBackgroundTexture", out object? c) && c is Color color)
                return new SolidColorBrush(color);

            return null;
        }
    }

    public IBrush? ForegroundBrush
    {
        get
        {
            if (TryGetColor("Foreground", out Color fg))
                return new SolidColorBrush(fg);
            if (TryGetColor("RemapColor", out Color remap))
                return new SolidColorBrush(remap);
            if (TryGetColor("IdleColor", out Color idle))
                return new SolidColorBrush(idle);
            if (TryGetColor("TextColor", out Color text))
                return new SolidColorBrush(text);

            if (!string.IsNullOrWhiteSpace(GetString("IdleTexture")) && ShowButtonText)
                return new SolidColorBrush(Color.FromRgb(255, 166, 72));

            return Brushes.White;
        }
    }

    /// <summary>Font family with CJK fallbacks (Inter lacks Chinese glyphs).</summary>
    public FontFamily LabelFontFamily { get; } = new("Microsoft YaHei UI, Segoe UI, Noto Sans CJK SC, sans-serif");

    public bool HasTextureImage => IdleImage != null || BackgroundImage != null;

    public Stretch BackgroundImageStretch { get; private set; } = Stretch.Fill;

    public bool UseTiledBackground { get; private set; }

    public IBrush? TiledBackgroundBrush { get; private set; }

    public Stretch PreviewImageStretch { get; private set; } = Stretch.Uniform;

    public IReadOnlyDictionary<string, string> Extensions => Node.RawAttributes;

    public string? GetIniString(string key)
    {
        if (Node.Props.TryGetValue(key, out object? value) && value != null)
            return value.ToString();

        return Node.RawAttributes.GetValueOrDefault(key);
    }

    public void SetDisplayText(string text)
    {
        _displayTextOverride = text;
        OnPropertyChanged(nameof(Text));
    }

    public void SetComboItems(IEnumerable<string> items)
    {
        UseComboItemIcons = false;
        ComboItemEntries.Clear();
        ComboItems.Clear();
        foreach (string item in items)
            ComboItems.Add(item);

        OnPropertyChanged(nameof(ComboItems));
        OnPropertyChanged(nameof(UseComboItemIcons));
        OnPropertyChanged(nameof(ComboItemEntries));
    }

    public void SetComboItemEntries(IEnumerable<ComboItemViewModel> items)
    {
        UseComboItemIcons = true;
        ComboItems.Clear();
        ComboItemEntries.Clear();
        foreach (ComboItemViewModel item in items)
        {
            ComboItemEntries.Add(item);
            ComboItems.Add(item.Text);
        }

        OnPropertyChanged(nameof(ComboItems));
        OnPropertyChanged(nameof(ComboItemEntries));
        OnPropertyChanged(nameof(UseComboItemIcons));
    }

    public void SetListItems(IEnumerable<string> items)
    {
        UseCatalogListItems = false;
        CatalogListItems.Clear();
        ListItems.Clear();
        foreach (string item in items)
            ListItems.Add(item);

        OnPropertyChanged(nameof(ListItems));
        OnPropertyChanged(nameof(UseCatalogListItems));
    }

    public void SetCatalogListItems(IEnumerable<CatalogListItemViewModel> items)
    {
        UseCatalogListItems = true;
        ListItems.Clear();
        CatalogListItems.Clear();
        foreach (CatalogListItemViewModel item in items)
            CatalogListItems.Add(item);

        OnPropertyChanged(nameof(CatalogListItems));
        OnPropertyChanged(nameof(UseCatalogListItems));
    }

    public void SetSideIcon(Bitmap? bitmap)
    {
        SideIcon = bitmap;
        OnPropertyChanged(nameof(SideIcon));
        OnPropertyChanged(nameof(HasSideIcon));
    }

    public void SetTabSelected(bool selected)
    {
        Node.Props["IsTabSelected"] = selected;
        OnPropertyChanged(nameof(IsTabSelected));
    }

    public void SetThumbImage(Bitmap? bitmap)
    {
        ThumbImage = bitmap;
        OnPropertyChanged(nameof(ThumbImage));
        OnPropertyChanged(nameof(HasThumbImage));
    }

    public bool HasSideIcon => SideIcon != null;

    public void SetButtonTextures(Bitmap? idle, Bitmap? hover)
    {
        IdleImage = idle;
        HoverImage = hover;
        OnPropertyChanged(nameof(IdleImage));
        OnPropertyChanged(nameof(HoverImage));
        OnPropertyChanged(nameof(HasTextureImage));
        OnPropertyChanged(nameof(ForegroundBrush));
    }

    public void SetPreviewImage(Bitmap? bitmap)
    {
        PreviewImage = bitmap;
        OnPropertyChanged(nameof(PreviewImage));
        OnPropertyChanged(nameof(HasPreviewImage));
    }

    public bool HasPreviewImage => PreviewImage != null;

    public void RefreshLayout()
    {
        OnPropertyChanged(nameof(CanvasLeft));
        OnPropertyChanged(nameof(CanvasTop));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(ScrollContentHeight));
        OnPropertyChanged(nameof(LayoutCanvasHeight));
        OnPropertyChanged(nameof(ZIndex));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsTabSelected));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(ForegroundBrush));
        LoadImages();

        foreach (UiNodeViewModel child in Children)
            child.RefreshLayout();
    }

    public void SetCanvasPosition(double left, double top)
    {
        Node.Props["CanvasLeft"] = left;
        Node.Props["CanvasTop"] = top;
        OnPropertyChanged(nameof(CanvasLeft));
        OnPropertyChanged(nameof(CanvasTop));
    }

    private ObservableCollection<string> BuildComboItems()
    {
        if (!Node.Props.TryGetValue("Items", out object? itemsObj) || itemsObj is not string itemsText)
            return [];

        string[] parts = itemsText.Split(',');
        return new ObservableCollection<string>(parts.Select(p => p.Trim()).Where(p => p.Length > 0));
    }

    private bool ReadInitialIsChecked()
        => Node.Props.TryGetValue("IsChecked", out object? v) && v is bool b && b;

    private bool ReadInitialIsEnabled()
        => !Node.Props.TryGetValue("IsEnabled", out object? v) || v is not bool b || b;

    private int ReadInitialSelectedIndex()
    {
        if (Node.Props.TryGetValue("SelectedIndex", out object? si) && si is int selected)
            return selected;
        return Node.Props.TryGetValue("DefaultIndex", out object? v) && v is int i ? i : 0;
    }

    private void LoadImages()
    {
        IdleImage = _resources.LoadBitmap(GetString("IdleTexture"));
        HoverImage = _resources.LoadBitmap(GetString("HoverTexture"));
        BackgroundImage = _resources.LoadBitmap(GetString("Background") ?? GetString("BackgroundTexture"));
        CheckBoxClearImage = _resources.LoadBitmap("checkBoxClear.png");
        CheckBoxCheckedImage = _resources.LoadBitmap("checkBoxChecked.png");

        if (IsButtonLike() && IdleImage == null)
            GameAssetResolver.ApplyStandardButtonTextures(this, _resources);

        ApplyDrawModePresentation();

        if (ShouldMatchTextureSize() && IdleImage != null)
        {
            Node.Props["Width"] = (double)IdleImage.PixelSize.Width;
            Node.Props["Height"] = (double)IdleImage.PixelSize.Height;
        }

        OnPropertyChanged(nameof(IdleImage));
        OnPropertyChanged(nameof(HoverImage));
        OnPropertyChanged(nameof(BackgroundImage));
        OnPropertyChanged(nameof(CheckBoxClearImage));
        OnPropertyChanged(nameof(CheckBoxCheckedImage));
        OnPropertyChanged(nameof(BackgroundBrush));
        OnPropertyChanged(nameof(HasTextureImage));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(BackgroundImageStretch));
        OnPropertyChanged(nameof(UseTiledBackground));
        OnPropertyChanged(nameof(TiledBackgroundBrush));
        OnPropertyChanged(nameof(PreviewImageStretch));
    }

    private void ApplyDrawModePresentation()
    {
        IniBackgroundDrawMode backgroundMode = IniDrawMode.Parse(GetIniString("DrawMode"));
        BackgroundImageStretch = IniDrawMode.ToImageStretch(backgroundMode);
        UseTiledBackground = backgroundMode == IniBackgroundDrawMode.Tiled;
        TiledBackgroundBrush = UseTiledBackground ? IniDrawMode.CreateTiledBrush(BackgroundImage) : null;

        IniBackgroundDrawMode previewMode = IniDrawMode.Parse(
            GetIniString("PreviewDrawMode") ?? GetIniString("DrawMode"));
        PreviewImageStretch = previewMode == IniBackgroundDrawMode.Stretched
            ? Stretch.Fill
            : IniDrawMode.ToImageStretch(previewMode);
    }

    private bool IsButtonLike()
        => ControlType.Contains("Button", StringComparison.OrdinalIgnoreCase)
           || Id.StartsWith("btn", StringComparison.OrdinalIgnoreCase);

    private bool ShouldMatchTextureSize()
        => Node.Props.TryGetValue("MatchTextureSize", out object? v) && v is bool b && b;

    private string? GetString(string key)
        => Node.Props.TryGetValue(key, out object? v) ? v?.ToString() : null;

    private bool TryGetColor(string key, out Color color)
    {
        if (Node.Props.TryGetValue(key, out object? v) && v is Color c)
        {
            color = c;
            return true;
        }

        color = default;
        return false;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
