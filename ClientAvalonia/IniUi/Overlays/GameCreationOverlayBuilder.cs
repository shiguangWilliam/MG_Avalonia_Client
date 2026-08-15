using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientCore;
using ClientCore.Extensions;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.IniUi.Overlays;

/// <summary>Create-game dialog (XNA GameCreationWindow subset, programmatic fallback).</summary>
public sealed class GameCreationOverlayContext
{
    public required TextBox RoomNameBox { get; init; }

    public required ComboBox MaxPlayersBox { get; init; }

    public required ComboBox SkillLevelBox { get; init; }

    public required CheckBox RequiresPasswordCheckBox { get; init; }

    public required TextBox PasswordBox { get; init; }

    public required Button CreateButton { get; init; }

    public required Button CancelButton { get; init; }

    public required TextBlock TunnelSummaryText { get; init; }

    public required Button MoreOptionsButton { get; init; }

    public Control TunnelPickerOverlay { get; set; } = null!;

    public CnCNetTunnel? SelectedTunnel { get; set; }

    /// <summary>True after the user clicks a tunnel row — stops auto-following TunnelSorter best.</summary>
    public bool UserManuallySelectedTunnel { get; set; }

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
        IReadOnlyList<CnCNetTunnel> tunnels,
        CnCNetTunnel? preferredTunnel = null)
    {
        var roomName = CreateTextBox($"{AppState.Environment.PlayerName}'s Game");
        var maxPlayers = CreateComboBox();
        for (int i = 8; i > 1; i--)
            maxPlayers.Items.Add(i.ToString());
        maxPlayers.SelectedIndex = 0;

        var skillLevel = CreateComboBox();
        string[] skillOptions = AppState.Configuration.Legacy.SkillLevelOptions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < skillOptions.Length; i++)
        {
            string localized = skillOptions[i].L10N($"INI:ClientDefinitions:SkillLevel:{i}");
            skillLevel.Items.Add(localized);
        }

        skillLevel.SelectedIndex = Math.Clamp(
            AppState.Configuration.Legacy.DefaultSkillLevelIndex,
            0,
            Math.Max(0, skillOptions.Length - 1));

        var password = CreateTextBox(string.Empty);
        password.IsEnabled = false;
        var requiresPassword = new CheckBox
        {
            Content = "Password protect this game".L10N("Client:Main:PasswordProtectGame"),
            Foreground = TitleBrush,
            FontSize = 12,
            IsChecked = false,
        };
        requiresPassword.IsCheckedChanged += (_, _) =>
        {
            bool enabled = requiresPassword.IsChecked == true;
            password.IsEnabled = enabled;
            if (!enabled)
                password.Text = string.Empty;
        };
        var createButton = CreatePrimaryButton("Create Game".L10N("Client:Main:CreateGame"));
        var cancelButton = CreateSecondaryButton("Cancel".L10N("Client:Main:ButtonCancel"));
        var moreOptionsButton = CreateSecondaryButton("More options...".L10N("Client:Main:AdvancedOptions"));
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
            RequiresPasswordCheckBox = requiresPassword,
            PasswordBox = password,
            CreateButton = createButton,
            CancelButton = cancelButton,
            TunnelSummaryText = tunnelSummary,
            MoreOptionsButton = moreOptionsButton,
        };

        // Prefer TunnelSorter best (lowest ping); fall back to first Official / first entry.
        CnCNetTunnel? initial = preferredTunnel;
        if (initial == null || !tunnels.Any(t => ReferenceEquals(t, initial)
                || (t.Address.Equals(initial.Address, StringComparison.OrdinalIgnoreCase) && t.Port == initial.Port)))
        {
            int defaultIndex = tunnels.ToList().FindIndex(t => t.Official);
            if (defaultIndex < 0 && tunnels.Count > 0)
                defaultIndex = 0;
            initial = defaultIndex >= 0 && defaultIndex < tunnels.Count ? tunnels[defaultIndex] : null;
        }
        else
        {
            // Resolve to the list instance so Tag/selection comparisons stay reference-stable.
            initial = tunnels.First(t =>
                ReferenceEquals(t, preferredTunnel)
                || (t.Address.Equals(preferredTunnel!.Address, StringComparison.OrdinalIgnoreCase)
                    && t.Port == preferredTunnel.Port));
        }

        context.SelectedTunnel = initial;

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
                CreateSectionTitle("Room settings".L10N("Client:Main:RoomSettings")),
                CreateField("Game room name:".L10N("Client:Main:GameRoomName"), roomName),
                CreateField("Maximum number of players:".L10N("Client:Main:GameMaxPlayerCount"), maxPlayers),
                CreateField(
                    "Select preferred skill level of players:".L10N("Client:Main:SelectSkillLevel"),
                    skillLevel),
                CreatePasswordSection(requiresPassword, password),
                CreateSectionTitle("Tunnel server:".L10N("Client:Main:TunnelServer")),
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
            message = "Game room name is required.".L10N("Client:Main:GameRoomNameRequired");
            return null;
        }

        if (context.MaxPlayersBox.SelectedItem is not string maxText || !int.TryParse(maxText, out int maxPlayers))
            maxPlayers = 8;

        if (context.SelectedTunnel == null)
        {
            message = "Select a tunnel server.".L10N("Client:Main:SelectTunnelServerPrompt");
            return null;
        }

        bool requiresPassword = context.RequiresPasswordCheckBox.IsChecked == true;
        string password = (context.PasswordBox.Text ?? string.Empty).Trim();
        if (requiresPassword && string.IsNullOrWhiteSpace(password))
        {
            message = "Enter a password or disable password protection."
                .L10N("Client:Main:PasswordRequiredOrDisable");
            return null;
        }

        return new CnCNetGameCreationRequest
        {
            RoomName = roomName,
            MaxPlayers = maxPlayers,
            RequiresPassword = requiresPassword,
            Password = requiresPassword ? password : string.Empty,
            Tunnel = context.SelectedTunnel,
            SkillLevel = Math.Max(0, context.SkillLevelBox.SelectedIndex),
        };
    }

    private static StackPanel CreatePasswordSection(CheckBox requiresPassword, TextBox passwordBox)
    {
        var caption = new TextBlock
        {
            Text = "Password (leave blank for none):".L10N("Client:Main:PasswordTextBlankForNone"),
            FontSize = 12,
            Foreground = MutedTextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        };

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                requiresPassword,
                caption,
                passwordBox,
            },
        };
    }

    private static Control CreateTunnelPickerOverlay(
        GameCreationOverlayContext context,
        IReadOnlyList<CnCNetTunnel> tunnels)
    {
        var tunnelRowsPanel = new StackPanel { Spacing = 6 };
        context.TunnelRows.Clear();

        for (int i = 0; i < tunnels.Count; i++)
        {
            CnCNetTunnel tunnel = tunnels[i];
            bool selected = context.SelectedTunnel != null
                && context.SelectedTunnel.Address == tunnel.Address
                && context.SelectedTunnel.Port == tunnel.Port;
            Border row = CreateTunnelRow(context, tunnel, selected);
            context.TunnelRows.Add(row);
            tunnelRowsPanel.Children.Add(row);
        }

        if (tunnels.Count == 0)
            tunnelRowsPanel.Children.Add(CreateHintLabel(
                "No tunnel servers available.".L10N("Client:Main:NoTunnelServers")));

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = tunnelRowsPanel,
        };

        var confirmButton = CreatePrimaryButton("Confirm".L10N("Client:Main:ButtonConfirm"));
        confirmButton.MinWidth = 110;
        var cancelButton = CreateSecondaryButton("Cancel".L10N("Client:Main:ButtonCancel"));
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
            Text = "Select tunnel server:".L10N("Client:Main:SelectTunnelServer"),
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
        if (context.SelectedTunnel == null)
        {
            context.TunnelSummaryText.Text =
                "No tunnel server selected — open More options to choose one."
                    .L10N("Client:Main:NoTunnelSelectedHint");
            return;
        }

        string ping = FormatPing(context.SelectedTunnel.PingInMs);
        context.TunnelSummaryText.Text =
            $"{context.SelectedTunnel.Name}  ({context.SelectedTunnel.Address}:{context.SelectedTunnel.Port})  ·  {ping}";
    }

    private static string FormatPing(int pingInMs)
        => pingInMs >= 0
            ? $"{pingInMs} ms"
            : "Unknown".L10N("Client:Main:UnknownPing");

    private static Border CreateTunnelRow(GameCreationOverlayContext context, CnCNetTunnel tunnel, bool selected)
    {
        var badge = new Border
        {
            Background = tunnel.Official ? OfficialBadgeBg : CommunityBadgeBg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2),
            Child = new TextBlock
            {
                Text = tunnel.Official
                    ? "Official".L10N("Client:Main:OfficialHeader")
                    : "Community".L10N("Client:Main:CommunityTunnel"),
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

        var pingText = new TextBlock
        {
            Text = FormatPing(tunnel.PingInMs),
            FontSize = 11,
            Foreground = MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 64,
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
            ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto,12,Auto"),
        };
        Grid.SetColumn(badge, 0);
        Grid.SetColumn(nameText, 2);
        Grid.SetColumn(pingText, 3);
        Grid.SetColumn(addressText, 5);
        grid.Children.Add(badge);
        grid.Children.Add(nameText);
        grid.Children.Add(pingText);
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

    private static void SelectTunnelRow(GameCreationOverlayContext context, Border selectedRow, CnCNetTunnel tunnel)
    {
        context.UserManuallySelectedTunnel = true;
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

    /// <summary>Refresh ping labels after ICMP results land (TunnelSorter / session StateChanged).</summary>
    public static void RefreshTunnelPings(GameCreationOverlayContext context)
    {
        UpdateTunnelSummary(context);
        foreach (Border row in context.TunnelRows)
        {
            if (row.Tag is not CnCNetTunnel tunnel || row.Child is not Grid grid)
                continue;

            foreach (Control child in grid.Children)
            {
                if (child is TextBlock tb && Grid.GetColumn(tb) == 3)
                    tb.Text = FormatPing(tunnel.PingInMs);
            }
        }
    }

    /// <summary>
    /// Follow <see cref="ClientAvalonia.CnCNet.Tunnels.TunnelSorter"/> best while the user has not
    /// manually picked a row.
    /// </summary>
    public static void ApplyPreferredTunnel(GameCreationOverlayContext context, CnCNetTunnel preferred)
    {
        if (context.UserManuallySelectedTunnel || preferred == null)
            return;

        Border? match = context.TunnelRows.FirstOrDefault(row =>
            row.Tag is CnCNetTunnel t
            && t.Address.Equals(preferred.Address, StringComparison.OrdinalIgnoreCase)
            && t.Port == preferred.Port);

        if (match == null || match.Tag is not CnCNetTunnel tunnel)
            return;

        context.SelectedTunnel = tunnel;
        foreach (Border row in context.TunnelRows)
        {
            bool isSelected = ReferenceEquals(row, match);
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
            ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto,12,Auto"),
            Margin = new Thickness(0, 0, 0, 4),
        };

        void AddHeader(string text, int column, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = MutedTextBrush,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = align,
            };
            Grid.SetColumn(tb, column);
            grid.Children.Add(tb);
        }

        AddHeader("Official".L10N("Client:Main:OfficialHeader"), 0);
        AddHeader("Name".L10N("Client:Main:NameHeader"), 2);
        AddHeader("Ping".L10N("Client:Main:PingHeader"), 3, HorizontalAlignment.Right);
        AddHeader("Address".L10N("Client:Main:AddressHeader"), 5, HorizontalAlignment.Right);
        return grid;
    }

    private static TextBlock CreateHeader() => new()
    {
        Text = "Create Game Room".L10N("Client:Main:CreateGameRoom"),
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
