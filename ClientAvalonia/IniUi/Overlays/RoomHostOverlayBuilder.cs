using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientCore;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.IniUi.Overlays;

/// <summary>Minimal host-side overlays for in-room tunnel change and lobby settings.</summary>
public static class RoomHostOverlayBuilder
{
    private static readonly IBrush WindowBg = new SolidColorBrush(Color.FromArgb(252, 16, 12, 8));
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.FromRgb(107, 78, 46));
    private static readonly IBrush TitleBrush = new SolidColorBrush(Color.FromRgb(242, 230, 216));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(255, 140, 50));
    private static readonly IBrush FieldBg = new SolidColorBrush(Color.FromArgb(220, 10, 8, 6));

    public static (Control Root, Size PreferredSize) BuildTunnelPicker(
        IReadOnlyList<CnCNetTunnel> tunnels,
        Action<CnCNetTunnel> onSelected,
        Action onCancel)
    {
        var list = new ListBox
        {
            Height = 260,
            Background = FieldBg,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
        };

        foreach (CnCNetTunnel tunnel in tunnels)
        {
            list.Items.Add($"{tunnel.Name}  —  {tunnel.Address}:{tunnel.Port}" +
                           (tunnel.PingInMs >= 0 ? $"  ({tunnel.PingInMs} ms)" : string.Empty));
        }

        if (list.Items.Count > 0)
            list.SelectedIndex = 0;

        var apply = CreateButton("Apply", () =>
        {
            int idx = list.SelectedIndex;
            if (idx >= 0 && idx < tunnels.Count)
                onSelected(tunnels[idx]);
        });
        var cancel = CreateButton("Cancel", onCancel);

        var root = new Border
        {
            Background = WindowBg,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Select tunnel server:",
                        Foreground = TitleBrush,
                        FontSize = 14,
                        FontWeight = FontWeight.Bold,
                    },
                    list,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { apply, cancel },
                    },
                },
            },
        };

        return (root, new Size(480, 360));
    }

    public static (Control Root, Size PreferredSize) BuildGameLobbySettings(
        CnCNetActiveGameRoom room,
        Action<string, int, int, string?> onApply,
        Action onCancel)
    {
        var nameBox = new TextBox
        {
            Text = room.RoomName,
            Background = FieldBg,
            Foreground = TitleBrush,
            BorderBrush = BorderBrush,
        };

        var maxPlayers = new ComboBox { Background = FieldBg, Foreground = TitleBrush };
        for (int i = 8; i >= 2; i--)
            maxPlayers.Items.Add(i.ToString());
        maxPlayers.SelectedIndex = Math.Clamp(8 - Math.Max(2, room.MaxPlayers), 0, maxPlayers.Items.Count - 1);

        var skill = new ComboBox { Background = FieldBg, Foreground = TitleBrush };
        string[] skillOptions = AppState.Configuration.Legacy.SkillLevelOptions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string option in skillOptions)
            skill.Items.Add(option);
        skill.SelectedIndex = Math.Clamp(room.SkillLevel, 0, Math.Max(0, skill.Items.Count - 1));

        var password = new TextBox
        {
            Text = room.Passworded ? room.Password : string.Empty,
            Background = FieldBg,
            Foreground = TitleBrush,
            BorderBrush = BorderBrush,
            Watermark = "(leave empty to clear password)",
        };

        var apply = CreateButton("Apply", () =>
        {
            string roomName = string.IsNullOrWhiteSpace(nameBox.Text) ? room.RoomName : nameBox.Text.Trim();
            int max = 8 - maxPlayers.SelectedIndex;
            if (maxPlayers.SelectedItem is string s && int.TryParse(s, out int parsed))
                max = parsed;
            int skillLevel = Math.Max(0, skill.SelectedIndex);
            string? pwd = password.Text;
            onApply(roomName, max, skillLevel, pwd);
        });
        var cancel = CreateButton("Cancel", onCancel);

        var root = new Border
        {
            Background = WindowBg,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Game lobby settings", Foreground = TitleBrush, FontSize = 14, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = "Room name", Foreground = TitleBrush, FontSize = 11 },
                    nameBox,
                    new TextBlock { Text = "Max players", Foreground = TitleBrush, FontSize = 11 },
                    maxPlayers,
                    new TextBlock { Text = "Skill level", Foreground = TitleBrush, FontSize = 11 },
                    skill,
                    new TextBlock { Text = "Password", Foreground = TitleBrush, FontSize = 11 },
                    password,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { apply, cancel },
                    },
                },
            },
        };

        return (root, new Size(420, 340));
    }

    private static Button CreateButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 88,
            Background = AccentBrush,
            Foreground = Brushes.Black,
            Padding = new Thickness(12, 6),
        };
        button.Click += (_, _) => onClick();
        return button;
    }
}
