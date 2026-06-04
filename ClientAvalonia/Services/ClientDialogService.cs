using Avalonia.Controls;

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

        var ok = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, MinWidth = 80 };
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
