using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using ClientCore.Extensions;

namespace ClientAvalonia.Services;

/// <summary>Avalonia modal dialogs replacing XNA XNAMessageBox for launch/errors (M5).</summary>
public static class ClientDialogService
{
    public static async Task ShowErrorAsync(Window? owner, string title, string message)
    {
        Window dialog = CreateDialog(owner, title, message);
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();
    }

    public static void ShowError(Window? owner, string title, string message)
    {
        Window dialog = CreateDialog(owner, title, message);
        if (owner != null)
            _ = dialog.ShowDialog(owner);
        else
            dialog.Show();
    }

    /// <summary>Password prompt for joining a password-protected CnCNet game (XNA PasswordRequestWindow).</summary>
    public static async Task<string?> ShowPasswordPromptAsync(Window? owner, string gameRoomName)
    {
        var window = new Window
        {
            Title = "Game Password",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            CanResize = false,
        };

        var passwordBox = new TextBox
        {
            PasswordChar = '*',
            Watermark = "Password",
            MinWidth = 320,
        };

        string? result = null;
        bool confirmed = false;

        var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(passwordBox.Text))
                return;

            confirmed = true;
            result = passwordBox.Text.Trim();
            window.Close();
        };

        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        cancel.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"Please enter the password for \"{gameRoomName}\" and click OK.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 380,
                },
                passwordBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 12,
                    Children = { ok, cancel },
                },
            },
        };

        passwordBox.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter)
                return;

            if (string.IsNullOrWhiteSpace(passwordBox.Text))
                return;

            confirmed = true;
            result = passwordBox.Text.Trim();
            window.Close();
        };

        if (owner != null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            window.Closed += (_, _) => tcs.TrySetResult(true);
            window.Show();
            await tcs.Task;
        }

        return confirmed ? result : null;
    }

    /// <summary>
    /// CnCNet ingress WAF warn dialog. Returns true if the player chose to add suggested block keys.
    /// </summary>
    public static async Task<bool> ShowWafAlertAsync(Window? owner, string title, string message, bool offerBlock)
    {
        var window = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            CanResize = false,
        };

        bool block = false;
        var dismiss = new Button { Content = "Got it".L10N("Client:Main:ButtonGotIt"), MinWidth = 90, IsDefault = true };
        dismiss.Click += (_, _) => window.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12,
            Children = { dismiss },
        };

        if (offerBlock)
        {
            var blockBtn = new Button { Content = "Add to blocklist".L10N("Client:Main:AddToBlocklist"), MinWidth = 120 };
            blockBtn.Click += (_, _) =>
            {
                block = true;
                window.Close();
            };
            buttons.Children.Insert(0, blockBtn);
        }

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 440,
                },
                buttons,
            },
        };

        if (owner != null)
            await window.ShowDialog(owner);
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            window.Closed += (_, _) => tcs.TrySetResult(true);
            window.Show();
            await tcs.Task;
        }

        return block;
    }

    /// <summary>Simple yes/no confirm. Returns true when the user confirms.</summary>
    public static async Task<bool> ConfirmAsync(Window? owner, string title, string message)
    {
        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            CanResize = false,
        };

        bool confirmed = false;
        var yes = new Button { Content = "OK".L10N("Client:Main:ButtonYes"), MinWidth = 90, IsDefault = true };
        yes.Click += (_, _) =>
        {
            confirmed = true;
            window.Close();
        };
        var no = new Button { Content = "Cancel".L10N("Client:Main:ButtonCancel"), MinWidth = 90, IsCancel = true };
        no.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 380,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 12,
                    Children = { yes, no },
                },
            },
        };

        if (owner != null)
            await window.ShowDialog(owner);
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            window.Closed += (_, _) => tcs.TrySetResult(true);
            window.Show();
            await tcs.Task;
        }

        return confirmed;
    }

    /// <summary>
    /// Preview/adjust WAF strategies: ID, content summary, mode (Off / Warning / Drop).
    /// </summary>
    public static async Task ShowWafStrategiesAsync(Window? owner, ClientAvalonia.CnCNet.Waf.ICnCNetIngressWaf waf)
    {
        var window = new Window
        {
            Title = "WAF Strategies".L10N("Client:Main:WafStrategiesTitle"),
            Width = 720,
            Height = 520,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            CanResize = true,
        };

        var list = new ListBox { MinHeight = 320 };

        var idText = new TextBlock { FontWeight = Avalonia.Media.FontWeight.Bold };
        var contentText = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 640,
        };
        var modeBox = new ComboBox
        {
            Width = 160,
            ItemsSource = new[]
            {
                "Off".L10N("Client:Main:WafModeOff"),
                "Warning",
                "Drop",
            },
            IsEnabled = false,
        };

        List<ClientAvalonia.CnCNet.Waf.WafStrategyRow> rows = waf.ListStrategies().ToList();
        list.ItemsSource = rows.Select(FormatStrategyLine).ToList();
        bool syncing = false;

        void SyncSelection()
        {
            int idx = list.SelectedIndex;
            if (idx < 0 || idx >= rows.Count)
            {
                idText.Text = "Strategy ID: -".L10N("Client:Main:WafStrategyIdNone");
                contentText.Text = "Strategy content: -".L10N("Client:Main:WafStrategyContentNone");
                modeBox.IsEnabled = false;
                return;
            }

            ClientAvalonia.CnCNet.Waf.WafStrategyRow row = rows[idx];
            idText.Text = string.Format(
                "Strategy ID: {0} ({1})".L10N("Client:Main:WafStrategyIdFmt"),
                row.Id,
                row.Kind);
            contentText.Text = string.Format(
                "Strategy content: {0}".L10N("Client:Main:WafStrategyContentFmt"),
                row.Content);
            modeBox.IsEnabled = true;
            syncing = true;
            modeBox.SelectedIndex = row.Mode switch
            {
                ClientAvalonia.CnCNet.Waf.WafStrategyMode.Off => 0,
                ClientAvalonia.CnCNet.Waf.WafStrategyMode.Drop => 2,
                _ => 1,
            };
            syncing = false;
        }

        list.SelectionChanged += (_, _) => SyncSelection();
        modeBox.SelectionChanged += (_, _) =>
        {
            if (syncing)
                return;

            int idx = list.SelectedIndex;
            if (idx < 0 || idx >= rows.Count || modeBox.SelectedIndex < 0)
                return;

            var mode = modeBox.SelectedIndex switch
            {
                0 => ClientAvalonia.CnCNet.Waf.WafStrategyMode.Off,
                2 => ClientAvalonia.CnCNet.Waf.WafStrategyMode.Drop,
                _ => ClientAvalonia.CnCNet.Waf.WafStrategyMode.Warn,
            };
            ClientAvalonia.CnCNet.Waf.WafStrategyRow row = rows[idx];
            if (row.Mode == mode)
                return;

            waf.SetStrategyMode(row.Id, mode);
            rows[idx] = new ClientAvalonia.CnCNet.Waf.WafStrategyRow
            {
                Id = row.Id,
                Kind = row.Kind,
                Content = row.Content,
                Mode = mode,
            };
            int keep = list.SelectedIndex;
            syncing = true;
            list.ItemsSource = rows.Select(FormatStrategyLine).ToList();
            list.SelectedIndex = keep;
            syncing = false;
        };

        var close = new Button { Content = "Close".L10N("Client:Main:ButtonClose"), MinWidth = 90, IsDefault = true };
        close.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Select a strategy to adjust its mode: Off (disabled) / Warning (prompt) / Drop (silent discard). Changes apply immediately and are written to Client/WafStrategyPrefs.json.".L10N("Client:Main:WafStrategiesHint"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 680,
                },
                list,
                idText,
                contentText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Mode: ".L10N("Client:Main:WafEnableStatus"),
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        modeBox,
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close },
                },
            },
        };

        if (rows.Count > 0)
            list.SelectedIndex = 0;
        else
            SyncSelection();

        if (owner != null)
            await window.ShowDialog(owner);
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            window.Closed += (_, _) => tcs.TrySetResult(true);
            window.Show();
            await tcs.Task;
        }
    }

    private static string FormatStrategyLine(ClientAvalonia.CnCNet.Waf.WafStrategyRow row)
    {
        string mode = row.Mode switch
        {
            ClientAvalonia.CnCNet.Waf.WafStrategyMode.Off => "Off".L10N("Client:Main:WafModeOff"),
            ClientAvalonia.CnCNet.Waf.WafStrategyMode.Drop => "Drop",
            _ => "Warning",
        };
        string content = row.Content.Length > 72 ? row.Content[..69] + "…" : row.Content;
        return $"[{mode}] {row.Id} — {content}";
    }

    /// <summary>DX <c>GameLoadingWindow</c>: pick a <c>*.SAV</c> to load / delete.</summary>
    public static async Task<SinglePlayerSavedGame?> ShowLoadGamePickerAsync(
        Window? owner,
        IReadOnlyList<SinglePlayerSavedGame> initialSaves)
    {
        var saves = initialSaves.ToList();
        var window = new Window
        {
            Title = "Load Game",
            Width = 560,
            Height = 420,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            CanResize = false,
        };

        var list = new ListBox
        {
            ItemsSource = saves.Select(s => $"{s.DisplayName}    {s.LastModified}").ToList(),
            MinHeight = 280,
        };

        SinglePlayerSavedGame? selected = null;
        bool load = false;

        var loadBtn = new Button { Content = "Load", MinWidth = 90, IsDefault = true, IsEnabled = false };
        var deleteBtn = new Button { Content = "Delete", MinWidth = 90, IsEnabled = false };
        var cancelBtn = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };

        list.SelectionChanged += (_, _) =>
        {
            bool ok = list.SelectedIndex >= 0 && list.SelectedIndex < saves.Count;
            loadBtn.IsEnabled = ok;
            deleteBtn.IsEnabled = ok;
        };

        loadBtn.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= saves.Count)
                return;
            selected = saves[list.SelectedIndex];
            load = true;
            window.Close();
        };

        deleteBtn.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= saves.Count)
                return;
            SinglePlayerSavedGame sg = saves[list.SelectedIndex];
            if (SinglePlayerSavedGameCatalog.TryDelete(sg.FileName))
            {
                saves = SinglePlayerSavedGameCatalog.ListSaves().ToList();
                list.ItemsSource = saves.Select(s => $"{s.DisplayName}    {s.LastModified}").ToList();
                list.SelectedIndex = -1;
            }
        };

        cancelBtn.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Select a saved game:" },
                list,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 12,
                    Children = { loadBtn, deleteBtn, cancelBtn },
                },
            },
        };

        if (owner != null)
            await window.ShowDialog(owner);
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            window.Closed += (_, _) => tcs.TrySetResult(true);
            window.Show();
            await tcs.Task;
        }

        return load ? selected : null;
    }

    private static Window CreateDialog(Window? owner, string title, string message)
    {
        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            CanResize = false,
        };

        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 80 };
        ok.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 },
                ok,
            },
        };

        return window;
    }
}
