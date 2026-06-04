using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Controls;

/// <summary>Selects Avalonia DataTemplate by UiNodeViewModel.TemplateKey.</summary>
public class DxTemplateSelector : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is not UiNodeViewModel vm)
            return new TextBlock { Text = "?" };

        if (Application.Current?.TryFindResource(vm.TemplateKey, out object? resource) == true
            && resource is IDataTemplate template)
            return template.Build(param);

        return new Border
        {
            Background = Avalonia.Media.Brushes.DarkRed,
            Child = new TextBlock { Text = vm.TemplateKey },
        };
    }

    public bool Match(object? data) => data is UiNodeViewModel;
}
