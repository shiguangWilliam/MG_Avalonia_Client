using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.Rendering;

public sealed class UiViewModelFactory
{
    private readonly ResourceResolver _resources;
    private readonly BehaviorRegistry _behaviors;

    public UiViewModelFactory(ResourceResolver resources, BehaviorRegistry behaviors)
    {
        _resources = resources;
        _behaviors = behaviors;
    }

    public UiNodeViewModel CreateTree(UiNodeTree tree) => CreateNode(tree.Root);

    public void RefreshTree(UiNodeViewModel root) => root.RefreshLayout();

    private UiNodeViewModel CreateNode(UiNode node)
    {
        var childVms = node.Children.Select(CreateNode).ToList();
        return new UiNodeViewModel(node, _resources, _behaviors, childVms);
    }
}
