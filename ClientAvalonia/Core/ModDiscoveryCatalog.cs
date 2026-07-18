namespace ClientAvalonia.Core;

/// <summary>
/// Builds mod-name discovery lists for the workspace picker (generalized, not MG-first).
/// </summary>
public static class ModDiscoveryCatalog
{
    /// <summary>
    /// Mod names to probe for workspace entries.
    /// Order: Avalonia registry (authoritative) → explicit override → optional DX hint keys.
    /// Never invents mod names from a hard-coded MG-only list when the caller did not ask for hints.
    /// </summary>
    public static IReadOnlyList<string> BuildModNamesToProbe(
        IReadOnlyList<string>? explicitCandidateKeys = null,
        bool includeLegacyDxHints = true)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name.Trim()))
                return;
            names.Add(name.Trim());
        }

        foreach (string registered in ModWorkspaceRegistry.ListRegisteredModNames())
            Add(registered);

        if (explicitCandidateKeys != null)
        {
            foreach (string key in explicitCandidateKeys)
                Add(key);
            return names;
        }

        if (includeLegacyDxHints)
        {
            foreach (string hint in ModWorkspaceRegistry.KnownDxHintModNames)
                Add(hint);
        }

        return names;
    }
}
