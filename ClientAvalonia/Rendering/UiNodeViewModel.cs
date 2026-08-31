using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Binding;
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
        ComboItems = [];
        InitializeComboFromIni();
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

    /// <summary>Raised when <see cref="IsChecked"/> changes via user/UI (not <see cref="SetIsCheckedSilent"/>).</summary>
    public event Action? CheckedChanged;

    public event Action? InputTextChanged;

    public ICommand ClickCommand { get; }

    public void InvokeClick() => _behaviors.Resolve(Id).OnClick(this);

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
        => TemplateKey is "DxOptionsScrollPanel" or "DxLobbyOptionsPanel" ? ScrollContentHeight : Height;
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

    /// <summary>Slider range (DX XNATrackbar MinValue/MaxValue). Defaults suit campaign difficulty (0–2).</summary>
    public double SliderMinimum => Node.GetNumericProp("MinValue", 0);

    public double SliderMaximum
    {
        get
        {
            double max = Node.GetNumericProp("MaxValue", 0);
            return max > 0 ? max : 2;
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
        set => SetIsChecked(value, notify: true);
    }

    public void SetIsCheckedSilent(bool value) => SetIsChecked(value, notify: false);

    private void SetIsChecked(bool value, bool notify)
    {
        if (_isChecked == value)
            return;

        _isChecked = value;
        Node.Props["IsChecked"] = value;
        OnPropertyChanged(nameof(IsChecked));

        if (notify)
            CheckedChanged?.Invoke();
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetSelectedIndex(value, notify: true);
    }

    public void SetSelectedIndexSilent(int value) => SetSelectedIndex(value, notify: false);

    private void SetSelectedIndex(int value, bool notify)
    {
        if (_selectedIndex == value)
            return;

        _selectedIndex = value;
        Node.Props["SelectedIndex"] = value;
        OnPropertyChanged(nameof(SelectedIndex));

        if (notify)
            SelectionChanged?.Invoke();
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

    /// <summary>Starting-location markers rendered over the map preview.</summary>
    public ObservableCollection<MapStartMarkerVm> StartMarkers { get; } = [];

    public bool HasStartMarkers => StartMarkers.Count > 0;

    /// <summary>Host can open an assign menu on marker left-click.</summary>
    public bool MapPreviewCanAssign { get; set; }

    /// <summary>Joiner can claim a free starting location by left-click.</summary>
    public bool MapPreviewCanSelectLocal { get; set; }

    /// <summary>Copied from the active <see cref="Domain.MapEntry.EnforceMaxPlayers"/>.</summary>
    public bool MapPreviewEnforceMaxPlayers { get; set; }

    public event Action<int>? StartMarkerLeftClicked;

    public event Action<int>? StartMarkerRightClicked;

    public void SetStartMarkers(IEnumerable<MapStartMarkerVm> markers)
    {
        StartMarkers.Clear();
        foreach (MapStartMarkerVm marker in markers)
            StartMarkers.Add(marker);
        OnPropertyChanged(nameof(StartMarkers));
        OnPropertyChanged(nameof(HasStartMarkers));
    }

    public void NotifyStartMarkerLeftClick(int startLocation1Based)
        => StartMarkerLeftClicked?.Invoke(startLocation1Based);

    public void NotifyStartMarkerRightClick(int startLocation1Based)
        => StartMarkerRightClicked?.Invoke(startLocation1Based);

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
        OnPropertyChanged(nameof(ShowButtonText));
        OnPropertyChanged(nameof(ButtonLabel));
    }

    public string? BriefingTitle { get; private set; }

    public string? BriefingLocation { get; private set; }

    public IReadOnlyList<string> BriefingObjectives { get; private set; } = [];

    public string? BriefingBody { get; private set; }

    public string? BriefingStatusHint { get; private set; }

    public bool UseStructuredBriefing { get; private set; }

    public bool HasBriefingTitle => !string.IsNullOrWhiteSpace(BriefingTitle);

    public bool HasBriefingLocation => !string.IsNullOrWhiteSpace(BriefingLocation);

    public bool HasBriefingObjectives => BriefingObjectives.Count > 0;

    public bool HasBriefingBody => !string.IsNullOrWhiteSpace(BriefingBody);

    public bool HasBriefingStatusHint => !string.IsNullOrWhiteSpace(BriefingStatusHint);

    public void SetMissionBriefing(MissionBriefingParsed briefing, string? statusHint = null)
    {
        BriefingTitle = briefing.Title;
        BriefingLocation = briefing.Location;
        BriefingObjectives = briefing.Objectives;
        BriefingBody = briefing.Body;
        BriefingStatusHint = statusHint;
        UseStructuredBriefing = briefing.IsStructured;
        _displayTextOverride = briefing.IsStructured
            ? BuildPlainBriefingFallback(briefing, statusHint)
            : AppendStatusHint(briefing.RawFallback, statusHint);

        OnPropertyChanged(nameof(BriefingTitle));
        OnPropertyChanged(nameof(BriefingLocation));
        OnPropertyChanged(nameof(BriefingObjectives));
        OnPropertyChanged(nameof(BriefingBody));
        OnPropertyChanged(nameof(BriefingStatusHint));
        OnPropertyChanged(nameof(UseStructuredBriefing));
        OnPropertyChanged(nameof(HasBriefingTitle));
        OnPropertyChanged(nameof(HasBriefingLocation));
        OnPropertyChanged(nameof(HasBriefingObjectives));
        OnPropertyChanged(nameof(HasBriefingBody));
        OnPropertyChanged(nameof(HasBriefingStatusHint));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(ShowButtonText));
        OnPropertyChanged(nameof(ButtonLabel));
    }

    private static string BuildPlainBriefingFallback(MissionBriefingParsed briefing, string? statusHint)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(briefing.Title))
            sb.AppendLine(briefing.Title);
        if (!string.IsNullOrWhiteSpace(briefing.Location))
            sb.AppendLine($"地点：{briefing.Location}");
        if (!string.IsNullOrWhiteSpace(briefing.Body))
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.AppendLine(briefing.Body);
        }

        if (briefing.Objectives.Count > 0)
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.AppendLine("任务目标：");
            for (int i = 0; i < briefing.Objectives.Count; i++)
                sb.AppendLine($"{i + 1}. {briefing.Objectives[i]}");
        }

        return AppendStatusHint(sb.ToString().Trim(), statusHint);
    }

    private static string AppendStatusHint(string text, string? statusHint)
    {
        if (string.IsNullOrWhiteSpace(statusHint))
            return text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return statusHint;
        return $"{text.TrimEnd()}\n\n{statusHint}";
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
        OnPropertyChanged(nameof(ShowButtonText));
        OnPropertyChanged(nameof(ButtonLabel));
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

    /// <summary>
    /// Build combo contents from INI <c>Items</c> / <c>ItemLabels</c>, matching DX
    /// <c>GameSessionDropDown</c>: labels for display, raw Items values kept in
    /// <see cref="ComboItemViewModel.Tag"/> for spawn/map-code writes.
    /// </summary>
    private void InitializeComboFromIni()
    {
        if (!Node.Props.TryGetValue("Items", out object? itemsObj) || itemsObj is not string itemsText)
            return;

        string[] items = SplitCommaList(itemsText);
        if (items.Length == 0)
            return;

        string[] labels = [];
        if (Node.Props.TryGetValue("ItemLabels", out object? labelsObj) && labelsObj is string labelsText)
            labels = SplitCommaList(labelsText);

        bool hasLabels = labels.Length > 0 && labels.Any(static l => l.Length > 0);
        if (!hasLabels)
        {
            foreach (string item in items)
                ComboItems.Add(item);
            return;
        }

        UseComboItemIcons = true;
        for (int i = 0; i < items.Length; i++)
        {
            string label = i < labels.Length && labels[i].Length > 0 ? labels[i] : items[i];
            var entry = new ComboItemViewModel
            {
                Text = label,
                Tag = items[i],
            };
            ComboItemEntries.Add(entry);
            ComboItems.Add(label);
        }
    }

    private static string[] SplitCommaList(string text)
        => text.Split(',')
            .Select(static p => p.Trim())
            .Where(static p => p.Length > 0)
            .ToArray();

    /// <summary>
    /// Value used for spawn.ini / map-code writes. Prefers <see cref="ComboItemViewModel.Tag"/>
    /// (raw INI Items path) when ItemLabels drove the display text.
    /// </summary>
    public string? GetSelectedComboValue()
    {
        if (SelectedIndex < 0)
            return null;

        if (UseComboItemIcons
            && SelectedIndex < ComboItemEntries.Count
            && !string.IsNullOrEmpty(ComboItemEntries[SelectedIndex].Tag))
        {
            return ComboItemEntries[SelectedIndex].Tag;
        }

        if (SelectedIndex < ComboItems.Count)
            return ComboItems[SelectedIndex];

        return null;
    }

    public void SetForeground(Color color)
    {
        Node.Props["Foreground"] = color;
        OnPropertyChanged(nameof(ForegroundBrush));
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
        // Tactical skin replaces the original PNG chrome with procedurally
        // generated textures (cold, hairline, token-driven).
        if (Themes.DxThemeManager.IsTactical)
        {
            ApplyTacticalTextures();
            ApplyDrawModePresentation();
            OnPropertyChanged(nameof(HasTextureImage));
            return;
        }

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

    /// <summary>
    /// Tactical mode: buttons get generated hairline textures; window roots get the
    /// cold chrome texture; checkboxes get generated boxes. Original assets are
    /// deliberately replaced everywhere for a unified style.
    /// </summary>
    private void ApplyTacticalTextures()
    {
        bool isButton = IsButtonLike();
        bool isMainMenuRoot = string.Equals(Node.WindowName, "MainMenu", StringComparison.OrdinalIgnoreCase)
            && Node.Parent is null;
        bool isWindowRoot = !isMainMenuRoot
            && (Node.Props.ContainsKey("DrawMode") || Node.Props.ContainsKey("BackgroundTexture"));

        if (isButton)
        {
            int width = (int)Math.Round(Math.Clamp(Width, 60, 600));
            (Bitmap idle, Bitmap hover) = Themes.TacticalAssetFactory.CreateButton(width);
            SetButtonTextures(idle, hover);
            Node.Props["Width"] = (double)idle.PixelSize.Width;
            Node.Props["Height"] = (double)idle.PixelSize.Height;
        }
        else if (isWindowRoot)
        {
            BackgroundImage = Themes.TacticalAssetFactory.CreateWindowChrome(
                (int)Math.Round(Width),
                (int)Math.Round(Height));
            BackgroundImageStretch = Stretch.Fill;
        }

        if (ControlType.Contains("CheckBox", StringComparison.OrdinalIgnoreCase))
        {
            CheckBoxClearImage = Themes.TacticalAssetFactory.CreateCheckbox(false);
            CheckBoxCheckedImage = Themes.TacticalAssetFactory.CreateCheckbox(true);
        }

        OnPropertyChanged(nameof(IdleImage));
        OnPropertyChanged(nameof(HoverImage));
        OnPropertyChanged(nameof(BackgroundImage));
        OnPropertyChanged(nameof(CheckBoxClearImage));
        OnPropertyChanged(nameof(CheckBoxCheckedImage));
        OnPropertyChanged(nameof(BackgroundImageStretch));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
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
