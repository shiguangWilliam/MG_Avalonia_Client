using Avalonia.Controls;
using Avalonia.Layout;

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
