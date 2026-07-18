using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClientAvalonia.Core;
using ClientAvalonia.Platform;
using ClientAvalonia.Services;
using ClientAvalonia.Views;
using Rampastring.Tools;

namespace ClientAvalonia;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Resources.MergedDictionaries.Add(
            (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://ClientAvalonia/Themes/DxControlStyles.axaml")));
        Resources.MergedDictionaries.Add(
            (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://ClientAvalonia/Themes/DxCampaignStyles.axaml")));
        Resources.MergedDictionaries.Add(
            (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://ClientAvalonia/Themes/DxOfficialTheme.axaml")));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ShowWorkspacePicker(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Startup gate / return-to-picker entry.</summary>
    public static void ShowWorkspacePicker(IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var picker = new WorkspacePickerWindow();
        picker.WorkspaceBound += () => OnWorkspaceBound(desktop);
        desktop.MainWindow = picker;

        // Startup lifetime may show MainWindow later; mid-session return MUST Show explicitly
        // or the only visible window is closed and the process looks "dead".
        if (!picker.IsVisible)
            picker.Show();

        picker.Activate();
    }

    private static void OnWorkspaceBound(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!ClientStartupService.BootstrapSucceeded)
        {
            string message =
                "ClientCore 初始化失败，地图、玩家名与 CnCNet 大厅可能无法使用。\n\n" +
                ClientStartupService.BootstrapError +
                "\n\n请检查 Resources\\ClientDefinitions.ini（MG 通常需要 ClientGameType=YR）。";
            ClientDialogService.ShowError(null, "Client startup failed", message);
            if (desktop.MainWindow is not WorkspacePickerWindow)
                ShowWorkspacePicker(desktop);
            return;
        }

        WindowsPlatformProfile.Apply(Application.Current!);

        Window? previous = desktop.MainWindow;
        var main = new MainWindow();
        desktop.MainWindow = main;
        main.Show();
        main.Activate();

        if (previous != null && !ReferenceEquals(previous, main))
            CloseWindowSafe(previous);
    }

    /// <summary>
    /// §5.2 return to mod picker.
    /// Order is enforced by <see cref="WorkspaceSessionHandoff"/> (show → close → teardown).
    /// </summary>
    public static void ReturnToWorkspacePicker()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        Window? previous = desktop.MainWindow;

        WorkspaceSessionHandoff.ExecuteReturnToPicker(
            ensureExplicitShutdown: () => desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown,
            showPicker: () => ShowWorkspacePicker(desktop),
            closePrevious: () =>
            {
                if (previous != null && !ReferenceEquals(previous, desktop.MainWindow))
                    CloseWindowSafe(previous);
            },
            teardownSession: () =>
            {
                try
                {
                    ModWorkspaceBinder.TeardownSession();
                }
                catch (Exception ex)
                {
                    Logger.Log($"ReturnToWorkspacePicker: teardown failed: {ex.Message}");
                }
            });
    }

    private static void CloseWindowSafe(Window window)
    {
        try
        {
            if (window.IsVisible)
                window.Hide();

            // Defer Close so we don't re-enter layout/input from the Switch-Mod click handler.
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    window.Close();
                }
                catch (Exception ex)
                {
                    Logger.Log($"CloseWindowSafe: Close failed: {ex.Message}");
                }
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Logger.Log($"CloseWindowSafe: Hide failed: {ex.Message}");
        }
    }
}
