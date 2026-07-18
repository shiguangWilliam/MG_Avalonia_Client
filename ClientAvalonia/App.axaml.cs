using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClientAvalonia.Core;
using ClientAvalonia.Platform;
using ClientAvalonia.Services;
using ClientAvalonia.Views;

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
        // Last-resort hook: OS-initiated shutdown (logoff, kill -SIGTERM on Linux, ProcessExit).
        // Fires after the Avalonia lifetime has already torn down UI, so we only flush the
        // non-Avalonia singletons (IRC sockets, timers) without calling desktop.Shutdown again.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutdownService.Shutdown("AppDomain.ProcessExit");

        ClientStartupService.Run();
        WindowsPlatformProfile.Apply(this);

        if (!ClientStartupService.BootstrapSucceeded)
        {
            string message =
                "ClientCore 初始化失败，地图、玩家名与 CnCNet 大厅可能无法使用。\n\n" +
                ClientStartupService.BootstrapError +
                "\n\n请检查 Resources\\ClientDefinitions.ini（MG 通常需要 ClientGameType=YR）。";
            ClientDialogService.ShowError(null, "Client startup failed", message);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
