using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClientAvalonia.Core;
using ClientAvalonia.Platform;
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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
