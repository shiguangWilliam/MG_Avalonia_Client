namespace ClientAvalonia.IniUi.Models;

public sealed class UiNodeTree
{
    public required UiNode Root { get; init; }

    public required string SourcePath { get; init; }

    public UiNode? FindNode(string id)
    {
        if (Root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return Root;

        return FindInChildren(Root, id);
    }

    private static UiNode? FindInChildren(UiNode node, string id)
    {
        foreach (UiNode child in node.Children)
        {
            if (child.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return child;

            UiNode? found = FindInChildren(child, id);
            if (found != null)
                return found;
        }

        return null;
    }

    public IEnumerable<UiNode> AllNodes()
    {
        yield return Root;
        foreach (UiNode child in EnumerateDescendants(Root))
            yield return child;
    }

    private static IEnumerable<UiNode> EnumerateDescendants(UiNode node)
    {
        foreach (UiNode child in node.Children)
        {
            yield return child;
            foreach (UiNode grand in EnumerateDescendants(child))
                yield return grand;
        }
    }
}
