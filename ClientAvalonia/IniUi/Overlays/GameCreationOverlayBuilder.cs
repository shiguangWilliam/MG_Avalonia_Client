using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClientAvalonia.CnCNet;
using ClientCore;

namespace ClientAvalonia.IniUi.Overlays;

/// <summary>Create-game dialog (XNA GameCreationWindow subset, programmatic fallback).</summary>
public sealed class GameCreationOverlayContext
{
    public required TextBox RoomNameBox { get; init; }

    public required ComboBox MaxPlayersBox { get; init; }

    public required ComboBox SkillLevelBox { get; init; }

    public required TextBox PasswordBox { get; init; }

    public required Button CreateButton { get; init; }

    public required Button CancelButton { get; init; }

    public required TextBlock TunnelSummaryText { get; init; }

    public required Button MoreOptionsButton { get; init; }

    public Control TunnelPickerOverlay { get; set; } = null!;

    public CnCNetTunnelEntry? SelectedTunnel { get; set; }

    internal List<Border> TunnelRows { get; init; } = [];
}

public static class GameCreationOverlayBuilder
{
    public const double DialogWidth = 560;

    public const double DialogHeight = 380;

    private static readonly IBrush WindowBg = new SolidColorBrush(Color.FromArgb(252, 16, 12, 8));
    private static readonly IBrush FieldBg = new SolidColorBrush(Color.FromArgb(220, 10, 8, 6));
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.FromRgb(107, 78, 46));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(255, 140, 50));
    private static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.FromRgb(184, 170, 154));
    private static readonly IBrush TitleBrush = new SolidColorBrush(Color.FromRgb(242, 230, 216));
    private static readonly IBrush RowSelectedBg = new SolidColorBrush(Color.FromArgb(200, 60, 40, 20));
    private static readonly IBrush RowNormalBg = new SolidColorBrush(Color.FromArgb(160, 24, 18, 12));
    private static readonly IBrush OfficialBadgeBg = new SolidColorBrush(Color.FromArgb(200, 40, 90, 50));
    private static readonly IBrush CommunityBadgeBg = new SolidColorBrush(Color.FromArgb(180, 70, 70, 70));
    private static readonly IBrush OverlayDimBrush = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0));

    public static (Control Root, GameCreationOverlayContext Context, Size PreferredSize) Build(
        IReadOnlyList<CnCNetTunnelEntry> tunnels)
    {
        var roomName = CreateTextBox($"{ProgramConstants.PLAYERNAME}'s Game");
        var maxPlayers = CreateComboBox();
        for (int i = 8; i > 1; i--)
            maxPlayers.Items.Add(i.ToString());
        maxPlayers.SelectedIndex = 0;

        var skillLevel = CreateComboBox();
        string[] skillOptions = ClientConfiguration.Instance.SkillLevelOptions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string option in skillOptions)
            skillLevel.Items.Add(option);
        skillLevel.SelectedIndex = Math.Clamp(ClientConfiguration.Instance.DefaultSkillLevelIndex, 0, Math.Max(0, skillOptions.Length - 1));

        var password = CreateTextBox(string.Empty);
        var createButton = CreatePrimaryButton("Create Game");
        var cancelButton = CreateSecondaryButton("Cancel");
        var moreOptionsButton = CreateSecondaryButton("More options…");
        var tunnelSummary = new TextBlock
        {
            FontSize = 12,
            Foreground = TitleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var context = new GameCreationOverlayContext
        {
            RoomNameBox = roomName,
            MaxPlayersBox = maxPlayers,
            SkillLevelBox = skillLevel,
            PasswordBox = password,
            CreateButton = createButton,
            CancelButton = cancelButton,
            TunnelSummaryText = tunnelSummary,
            MoreOptionsButton = moreOptionsButton,
        };

        int defaultIndex = tunnels.ToList().FindIndex(t => t.Official);
        if (defaultIndex < 0 && tunnels.Count > 0)
            defaultIndex = 0;

        if (defaultIndex >= 0 && defaultIndex < tunnels.Count)
            context.SelectedTunnel = tunnels[defaultIndex];

        Control tunnelPickerOverlay = CreateTunnelPickerOverlay(context, tunnels);
        context.TunnelPickerOverlay = tunnelPickerOverlay;
        UpdateTunnelSummary(context);

        moreOptionsButton.Click += (_, _) =>
        {
            if (tunnels.Count == 0)
                return;

            tunnelPickerOverlay.IsVisible = true;
        };

        var formBody = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                CreateSectionTitle("Room settings"),
                CreateField("Game room name", roomName),
                CreateField("Maximum players", maxPlayers),
                CreateField("Skill level", skillLevel),
                CreateField("Password (optional)", password),
                CreateSectionTitle("Tunnel server"),
                CreateTunnelSummaryRow(tunnelSummary, moreOptionsButton),
            },
        };

        var bodyScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = formBody,
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,12,Auto"),
            Margin = new Thickness(0, 16, 0, 0),
        };
        Grid.SetColumn(createButton, 1);
        Grid.SetColumn(cancelButton, 3);
        footer.Children.Add(createButton);
        footer.Children.Add(cancelButton);

        var layout = new Grid
        {
            Width = DialogWidth - 52,
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
        };

        var header = CreateHeader();
        var headerRule = new Border
        {
            Height = 1,
            Background = BorderBrush,
            Margin = new Thickness(0, 0, 0, 16),
        };

        Grid.SetRow(header, 0);
        Grid.SetRow(headerRule, 1);
        Grid.SetRow(bodyScroll, 2);
        Grid.SetRow(footer, 3);
        layout.Children.Add(header);
        layout.Children.Add(headerRule);
        layout.Children.Add(bodyScroll);
        layout.Children.Add(footer);

        var innerCard = new Border
        {
            Background = WindowBg,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Child = layout,
        };

        var shell = new Grid
        {
            Width = DialogWidth,
            Height = DialogHeight,
            Children =
            {
                new Border
                {
                    BorderBrush = AccentBrush,
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(2),
                    Background = WindowBg,
                    Child = innerCard,
                },
                tunnelPickerOverlay,
            },
        };

        return (shell, context, new Size(DialogWidth, DialogHeight));
    }

    public static CnCNetGameCreationRequest? TryBuildRequest(GameCreationOverlayContext context, out string message)
    {
        message = string.Empty;
        string roomName = context.RoomNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(roomName))
        {
            message = "Game room name is required.";
            return null;
        }

        if (context.MaxPlayersBox.SelectedItem is not string maxText || !int.TryParse(maxText, out int maxPlayers))
            maxPlayers = 8;

        if (context.SelectedTunnel == null)
        {
            message = "Select a tunnel server.";
            return null;
        }

        return new CnCNetGameCreationRequest
        {
            RoomName = roomName,
            MaxPlayers = maxPlayers,
            Password = context.PasswordBox.Text ?? string.Empty,
            Tunnel = context.SelectedTunnel,
            SkillLevel = Math.Max(0, context.SkillLevelBox.SelectedIndex),
        };
    }

    private static Control CreateTunnelPickerOverlay(
        GameCreationOverlayContext context,
        IReadOnlyList<CnCNetTunnelEntry> tunnels)
    {
        var tunnelRowsPanel = new StackPanel { Spacing = 6 };
        context.TunnelRows.Clear();

        for (int i = 0; i < tunnels.Count; i++)
        {
            CnCNetTunnelEntry tunnel = tunnels[i];
            bool selected = context.SelectedTunnel != null
                && context.SelectedTunnel.Address == tunnel.Address
                && context.SelectedTunnel.Port == tunnel.Port;
            Border row = CreateTunnelRow(context, tunnel, selected);
            context.TunnelRows.Add(row);
            tunnelRowsPanel.Children.Add(row);
        }

        if (tunnels.Count == 0)
            tunnelRowsPanel.Children.Add(CreateHintLabel("No tunnel servers available."));

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = tunnelRowsPanel,
        };

        var confirmButton = CreatePrimaryButton("Confirm");
        confirmButton.MinWidth = 110;
        var cancelButton = CreateSecondaryButton("Cancel");
        cancelButton.MinWidth = 90;

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,10,Auto"),
            Margin = new Thickness(0, 4, 0, 0),
        };
        Grid.SetColumn(confirmButton, 1);
        Grid.SetColumn(cancelButton, 3);
        footer.Children.Add(confirmButton);
        footer.Children.Add(cancelButton);

        var pickerGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            MinHeight = 360,
        };

        var title = new TextBlock
        {
            Text = "Select tunnel server",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = TitleBrush,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var header = CreateTunnelHeader();

        Grid.SetRow(title, 0);
        Grid.SetRow(header, 1);
        Grid.SetRow(scroll, 2);
        Grid.SetRow(footer, 3);
        pickerGrid.Children.Add(title);
        pickerGrid.Children.Add(header);
        pickerGrid.Children.Add(scroll);
        pickerGrid.Children.Add(footer);

        var pickerCard = new Border
        {
            Width = DialogWidth - 24,
            Height = 440,
            Background = WindowBg,
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = pickerGrid,
        };

        var overlay = new Border
        {
            IsVisible = false,
            Background = OverlayDimBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ZIndex = 10,
            Child = pickerCard,
        };

        void ClosePicker()
        {
            overlay.IsVisible = false;
            UpdateTunnelSummary(context);
        }

        confirmButton.Click += (_, _) => ClosePicker();
        cancelButton.Click += (_, _) => ClosePicker();

        return overlay;
    }

    private static StackPanel CreateTunnelSummaryRow(TextBlock summary, Button moreButton)
    {
        moreButton.MinWidth = 120;
        moreButton.HorizontalAlignment = HorizontalAlignment.Right;
        moreButton.VerticalAlignment = VerticalAlignment.Top;

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        Grid.SetColumn(summary, 0);
        Grid.SetColumn(moreButton, 1);
        row.Children.Add(summary);
        row.Children.Add(moreButton);

        return new StackPanel
        {
            Spacing = 6,
            Children = { row },
        };
    }

    private static void UpdateTunnelSummary(GameCreationOverlayContext context)
    {
        context.TunnelSummaryText.Text = context.SelectedTunnel == null
            ? "No tunnel server selected — open More options to choose one."
            : $"{context.SelectedTunnel.Name}  ({context.SelectedTunnel.Address}:{context.SelectedTunnel.Port})";
    }

    private static Border CreateTunnelRow(GameCreationOverlayContext context, CnCNetTunnelEntry tunnel, bool selected)
    {
        var badge = new Border
        {
            Background = tunnel.Official ? OfficialBadgeBg : CommunityBadgeBg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2),
            Child = new TextBlock
            {
                Text = tunnel.Official ? "Official" : "Community",
                FontSize = 10,
                Foreground = Brushes.White,
            },
        };

        var nameText = new TextBlock
        {
            Text = tunnel.Name,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = TitleBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var addressText = new TextBlock
        {
            Text = $"{tunnel.Address}:{tunnel.Port}",
            FontSize = 11,
            Foreground = MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto"),
        };
        Grid.SetColumn(badge, 0);
        Grid.SetColumn(nameText, 2);
        Grid.SetColumn(addressText, 3);
        grid.Children.Add(badge);
        grid.Children.Add(nameText);
        grid.Children.Add(addressText);

        var row = new Border
        {
            Background = selected ? RowSelectedBg : RowNormalBg,
            BorderBrush = selected ? AccentBrush : BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10),
            MinHeight = 40,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = grid,
            Tag = tunnel,
        };

        if (selected)
            row.BoxShadow = BoxShadows.Parse("0 0 12 0 #60FF8C32");

        row.PointerPressed += (_, _) => SelectTunnelRow(context, row, tunnel);
        return row;
    }

    private static void SelectTunnelRow(GameCreationOverlayContext context, Border selectedRow, CnCNetTunnelEntry tunnel)
    {
        context.SelectedTunnel = tunnel;
        foreach (Border row in context.TunnelRows)
        {
            bool isSelected = ReferenceEquals(row, selectedRow);
            row.Background = isSelected ? RowSelectedBg : RowNormalBg;
            row.BorderBrush = isSelected ? AccentBrush : BorderBrush;
            row.BoxShadow = isSelected ? BoxShadows.Parse("0 0 12 0 #60FF8C32") : default;
        }

        UpdateTunnelSummary(context);
    }

    private static Grid CreateTunnelHeader()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto"),
            Margin = new Thickness(0, 0, 0, 4),
        };

        void AddHeader(string text, int column)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = MutedTextBrush,
                FontWeight = FontWeight.Bold,
            };
            Grid.SetColumn(tb, column);
            grid.Children.Add(tb);
        }

        AddHeader("TYPE", 0);
        AddHeader("SERVER", 2);
        var addr = new TextBlock
        {
            Text = "ADDRESS",
            FontSize = 10,
            Foreground = MutedTextBrush,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(addr, 3);
        grid.Children.Add(addr);
        return grid;
    }

    private static TextBlock CreateHeader() => new()
    {
        Text = "Create Game Room",
        FontSize = 18,
        FontWeight = FontWeight.Bold,
        Foreground = TitleBrush,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static TextBlock CreateSectionTitle(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        FontWeight = FontWeight.Bold,
        Foreground = AccentBrush,
        Margin = new Thickness(0, 2, 0, 0),
        LetterSpacing = 1,
    };

    private static TextBlock CreateHintLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = MutedTextBrush,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private static TextBox CreateTextBox(string text) => new()
    {
        Text = text,
        MinWidth = 240,
        MinHeight = 32,
        Background = FieldBg,
        Foreground = TitleBrush,
        BorderBrush = BorderBrush,
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10, 6),
    };

    private static ComboBox CreateComboBox() => new()
    {
        MinWidth = 240,
        MinHeight = 32,
        Background = FieldBg,
        Foreground = TitleBrush,
        BorderBrush = BorderBrush,
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10, 6),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static StackPanel CreateField(string label, Control input)
    {
        var caption = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = MutedTextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        };

        return new StackPanel
        {
            Spacing = 0,
            Children = { caption, input },
        };
    }

    private static Button CreatePrimaryButton(string text) => new()
    {
        Content = text,
        MinWidth = 130,
        MinHeight = 34,
        Background = AccentBrush,
        Foreground = Brushes.Black,
        FontWeight = FontWeight.Bold,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(16, 6),
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    private static Button CreateSecondaryButton(string text) => new()
    {
        Content = text,
        MinWidth = 100,
        MinHeight = 34,
        Background = FieldBg,
        Foreground = TitleBrush,
        BorderBrush = BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(16, 6),
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };
}
