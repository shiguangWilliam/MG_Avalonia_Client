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
        ClientStartupService.Run();
        WindowsPlatformProfile.Apply(this);

        if (!ClientStartupService.BootstrapSucceeded)
        {
            string message =
                "ClientCore failed to initialize. Maps, player name, and CnCNet lobby will not work.\n\n" +
                ClientStartupService.BootstrapError +
                "\n\nCheck Resources\\ClientDefinitions.ini (ClientGameType=YR for MG).";
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
