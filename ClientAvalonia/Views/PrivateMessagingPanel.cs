using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClientAvalonia.Views;

/// <summary>Minimal DX-aligned private messaging panel (Messages tab MVP).</summary>
public sealed class PrivateMessagingPanel : UserControl
{
    private readonly ListBox _peerList;
    private readonly ListBox _messageList;
    private readonly TextBox _input;
    private readonly TextBlock _title;
    private readonly TextBlock _status;
    private Action<string, string>? _send;
    private Action? _close;
    private Action<string>? _peerSelected;
    private Func<IReadOnlyList<(string Nick, int Unread)>>? _listPeers;
    private Func<string, IReadOnlyList<string>>? _listMessages;
    private string? _selectedNick;

    public PrivateMessagingPanel()
    {
        Width = 600;
        Height = 520;
        Background = new SolidColorBrush(Color.FromRgb(20, 16, 12));

        _title = new TextBlock
        {
            Text = "私信 (F4)",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 50)),
            Margin = new Thickness(12, 10, 12, 4),
        };

        _status = new TextBlock
        {
            Text = "选择左侧玩家后输入消息，按 Enter 发送。",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
            Margin = new Thickness(12, 0, 12, 8),
            TextWrapping = TextWrapping.Wrap,
        };

        _peerList = new ListBox
        {
            Width = 150,
            Background = new SolidColorBrush(Color.FromRgb(28, 24, 20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(85, 255, 140, 50)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(12, 0, 6, 12),
        };
        _peerList.SelectionChanged += OnPeerSelected;

        _messageList = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 24, 20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(85, 255, 140, 50)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 6),
        };

        _input = new TextBox
        {
            Watermark = "输入私信…",
            MaxLength = 200,
            Margin = new Thickness(0, 0, 12, 12),
            IsEnabled = false,
        };
        _input.KeyDown += OnInputKeyDown;

        var closeBtn = new Button
        {
            Content = "关闭",
            Padding = new Thickness(12, 4),
            Margin = new Thickness(0, 8, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        closeBtn.Click += (_, _) => _close?.Invoke();

        var right = new DockPanel();
        DockPanel.SetDock(_input, Dock.Bottom);
        right.Children.Add(_input);
        right.Children.Add(_messageList);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 0, 0, 0),
        };
        Grid.SetColumn(_peerList, 0);
        Grid.SetColumn(right, 1);
        body.Children.Add(_peerList);
        body.Children.Add(right);

        var root = new DockPanel();
        DockPanel.SetDock(_title, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        DockPanel.SetDock(closeBtn, Dock.Top);
        root.Children.Add(_title);
        root.Children.Add(closeBtn);
        root.Children.Add(_status);
        root.Children.Add(body);
        Content = root;
    }

    public void Bind(
        Func<IReadOnlyList<(string Nick, int Unread)>> listPeers,
        Func<string, IReadOnlyList<string>> listMessages,
        Action<string, string> send,
        Action close,
        Action<string>? peerSelected = null)
    {
        _listPeers = listPeers;
        _listMessages = listMessages;
        _send = send;
        _close = close;
        _peerSelected = peerSelected;
    }

    public string? SelectedNick => _selectedNick;

    public void Refresh(string? preferNick = null)
    {
        if (_listPeers == null || _listMessages == null)
            return;

        // Keep the currently selected peer unless the caller explicitly prefers another.
        string? keep = preferNick ?? _selectedNick;
        var peers = _listPeers();
        _peerList.ItemsSource = peers
            .Select(p => p.Unread > 0 ? $"{p.Nick} ({p.Unread})" : p.Nick)
            .ToList();

        int select = -1;
        if (!string.IsNullOrWhiteSpace(keep))
        {
            for (int i = 0; i < peers.Count; i++)
            {
                if (peers[i].Nick.Equals(keep, StringComparison.OrdinalIgnoreCase))
                {
                    select = i;
                    break;
                }
            }
        }

        if (select < 0 && peers.Count > 0)
            select = 0;

        _peerList.SelectedIndex = select;
        if (select >= 0)
            ShowPeer(peers[select].Nick);
        else
        {
            _selectedNick = null;
            _messageList.ItemsSource = null;
            _input.IsEnabled = false;
            _status.Text = "暂无私信会话。可在玩家列表双击玩家发起（后续），或等待对方私信。";
        }
    }

    public void FocusInput()
    {
        _input.Focus();
    }

    private void OnPeerSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_listPeers == null || _peerList.SelectedIndex < 0)
            return;

        var peers = _listPeers();
        if (_peerList.SelectedIndex >= peers.Count)
            return;

        ShowPeer(peers[_peerList.SelectedIndex].Nick);
    }

    private void ShowPeer(string nick)
    {
        _selectedNick = nick;
        _input.IsEnabled = true;
        _status.Text = $"与 {nick} 的私信";
        _peerSelected?.Invoke(nick);
        var items = _listMessages!(nick).ToList();
        _messageList.ItemsSource = items;
        if (items.Count > 0)
            _messageList.ScrollIntoView(items[^1]);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        string text = _input.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(_selectedNick))
            return;

        _send?.Invoke(_selectedNick, text);
        _input.Text = string.Empty;
        Refresh(_selectedNick);
        FocusInput();
    }
}
