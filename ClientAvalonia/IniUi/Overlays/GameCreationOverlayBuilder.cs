using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClientAvalonia.CnCNet;
using ClientCore;

namespace ClientAvalonia.IniUi.Overlays;

/// <summary>Create-game dialog (XNA GameCreationWindow subset).</summary>
public sealed class GameCreationOverlayContext
{
    public required TextBox RoomNameBox { get; init; }

    public required ComboBox MaxPlayersBox { get; init; }

    public required ComboBox SkillLevelBox { get; init; }

    public required TextBox PasswordBox { get; init; }

    public required ListBox TunnelList { get; init; }

    public required Border AdvancedPanel { get; init; }

    public required Button AdvancedButton { get; init; }

    public required Button CreateButton { get; init; }

    public required Button CancelButton { get; init; }

    public bool AdvancedVisible { get; set; }
}

public static class GameCreationOverlayBuilder
{
    private const int CompactHeight = 220;
    private const int AdvancedHeight = 420;
    private const int Width = 490;

    public static (Control Root, GameCreationOverlayContext Context) Build(IReadOnlyList<CnCNetTunnelEntry> tunnels)
    {
        var roomName = CreateTextBox($"{ProgramConstants.PLAYERNAME}'s Game");
        var maxPlayers = new ComboBox { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 8; i > 1; i--)
            maxPlayers.Items.Add(i.ToString());
        maxPlayers.SelectedIndex = 0;

        var skillLevel = new ComboBox { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
        string[] skillOptions = ClientConfiguration.Instance.SkillLevelOptions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string option in skillOptions)
            skillLevel.Items.Add(option);
        skillLevel.SelectedIndex = Math.Clamp(ClientConfiguration.Instance.DefaultSkillLevelIndex, 0, Math.Max(0, skillOptions.Length - 1));

        var password = CreateTextBox(string.Empty);

        var tunnelList = new ListBox
        {
            MinHeight = 160,
            MaxHeight = 160,
            IsVisible = false,
        };
        foreach (CnCNetTunnelEntry tunnel in tunnels)
        {
            tunnelList.Items.Add(new ListBoxItem
            {
                Content = FormatTunnelLine(tunnel),
                Tag = tunnel,
            });
        }

        if (tunnelList.Items.Count > 0)
            tunnelList.SelectedIndex = tunnels.ToList().FindIndex(t => t.Official) is int idx && idx >= 0 ? idx : 0;

        var advancedPanel = new Border
        {
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    CreateLabel("Tunnel server:"),
                    tunnelList,
                },
            },
        };

        var advancedButton = new Button
        {
            Content = "More Options",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 28,
        };

        var createButton = new Button { Content = "Create Game", MinWidth = 120, MinHeight = 28 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 120, MinHeight = 28 };

        var buttonRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetColumn(createButton, 0);
        Grid.SetColumn(cancelButton, 2);
        buttonRow.Children.Add(createButton);
        buttonRow.Children.Add(cancelButton);

        var form = new StackPanel
        {
            Spacing = 10,
            Width = Width,
            Children =
            {
                CreateField("Game room name:", roomName),
                CreateField("Maximum players:", maxPlayers),
                CreateField("Skill level:", skillLevel),
                CreateField("Password (leave blank for none):", password),
                advancedButton,
                advancedPanel,
                buttonRow,
            },
        };

        var root = new Border
        {
            Width = Width,
            Height = CompactHeight,
            Background = new SolidColorBrush(Color.FromArgb(230, 20, 16, 12)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(107, 78, 46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            Child = form,
        };

        var context = new GameCreationOverlayContext
        {
            RoomNameBox = roomName,
            MaxPlayersBox = maxPlayers,
            SkillLevelBox = skillLevel,
            PasswordBox = password,
            TunnelList = tunnelList,
            AdvancedPanel = advancedPanel,
            AdvancedButton = advancedButton,
            CreateButton = createButton,
            CancelButton = cancelButton,
            AdvancedVisible = false,
        };

        advancedButton.Click += (_, _) =>
        {
            context.AdvancedVisible = !context.AdvancedVisible;
            advancedPanel.IsVisible = context.AdvancedVisible;
            tunnelList.IsVisible = context.AdvancedVisible;
            advancedButton.Content = context.AdvancedVisible ? "Hide Options" : "More Options";
            root.Height = context.AdvancedVisible ? AdvancedHeight : CompactHeight;
        };

        return (root, context);
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

        if (roomName.Length > 23)
            roomName = roomName[..23];

        if (context.MaxPlayersBox.SelectedItem is not string maxText || !int.TryParse(maxText, out int maxPlayers))
            maxPlayers = 8;

        if (context.TunnelList.SelectedItem is not ListBoxItem { Tag: CnCNetTunnelEntry tunnel })
        {
            message = "Select a tunnel server.";
            return null;
        }

        return new CnCNetGameCreationRequest
        {
            RoomName = roomName,
            MaxPlayers = maxPlayers,
            Password = context.PasswordBox.Text ?? string.Empty,
            Tunnel = tunnel,
            SkillLevel = Math.Max(0, context.SkillLevelBox.SelectedIndex),
        };
    }

    private static TextBox CreateTextBox(string text) => new()
    {
        Text = text,
        MinWidth = 150,
        MinHeight = 24,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock CreateLabel(string text) => new()
    {
        Text = text,
        Foreground = Brushes.Gold,
        FontSize = 12,
    };

    private static Grid CreateField(string label, Control input)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        var caption = CreateLabel(label);
        caption.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(caption, 0);
        Grid.SetColumn(input, 1);
        grid.Children.Add(caption);
        grid.Children.Add(input);
        return grid;
    }

    private static string FormatTunnelLine(CnCNetTunnelEntry tunnel)
        => $"{tunnel.Name}  ·  Official: {(tunnel.Official ? "Yes" : "No")}  ·  {tunnel.Address}:{tunnel.Port}";
}
