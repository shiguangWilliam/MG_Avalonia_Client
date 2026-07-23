using System.Text.RegularExpressions;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>
/// Splits campaign <c>LongDescription</c> text into title / location / objectives / body
/// so the briefing panel can render structured chrome instead of a flat wall of text.
/// </summary>
public static partial class MissionBriefingParser
{
    private static readonly Regex LocationLine = LocationLineRegex();
    private static readonly Regex ObjectiveHeader = ObjectiveHeaderRegex();
    private static readonly Regex NumberedObjective = NumberedObjectiveRegex();

    public static MissionBriefingParsed Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return MissionBriefingParsed.Empty;

        string normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        string[] lines = normalized.Split('\n');

        string? title = null;
        string? location = null;
        var objectives = new List<string>();
        var bodyLines = new List<string>();
        bool inObjectives = false;
        bool sawStructure = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
            {
                if (!inObjectives && bodyLines.Count > 0)
                    bodyLines.Add(string.Empty);
                continue;
            }

            Match loc = LocationLine.Match(line);
            if (loc.Success)
            {
                location = loc.Groups[1].Value.Trim();
                sawStructure = true;
                inObjectives = false;
                continue;
            }

            if (ObjectiveHeader.IsMatch(line))
            {
                inObjectives = true;
                sawStructure = true;
                string remainder = ObjectiveHeader.Replace(line, string.Empty).Trim().TrimStart(':', '：').Trim();
                if (remainder.Length > 0)
                    objectives.Add(remainder);
                continue;
            }

            Match numbered = NumberedObjective.Match(line);
            if (numbered.Success && (inObjectives || objectives.Count > 0 || LooksLikeObjectiveBlock(lines, i)))
            {
                inObjectives = true;
                sawStructure = true;
                objectives.Add(numbered.Groups["text"].Value.Trim());
                continue;
            }

            if (inObjectives)
            {
                // Continuation line under an objective header without a new number.
                if (objectives.Count > 0)
                    objectives[^1] = $"{objectives[^1]} {line}".Trim();
                else
                    objectives.Add(line);
                continue;
            }

            if (title == null && i == 0 && bodyLines.Count == 0)
            {
                title = line;
                continue;
            }

            bodyLines.Add(line);
        }

        string body = string.Join('\n', TrimTrailingEmpty(bodyLines)).Trim();
        return new MissionBriefingParsed(
            Title: title,
            Location: location,
            Objectives: objectives,
            Body: body,
            RawFallback: normalized,
            IsStructured: sawStructure || !string.IsNullOrWhiteSpace(title));
    }

    private static bool LooksLikeObjectiveBlock(string[] lines, int index)
    {
        // Accept numbered lines near the end of the briefing as objectives even without a header.
        int remaining = lines.Length - index;
        return remaining <= 8;
    }

    private static List<string> TrimTrailingEmpty(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    [GeneratedRegex(@"^(?:地点|Location)\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationLineRegex();

    [GeneratedRegex(@"^(?:任务目标|任务目的|目标|Objectives?|Mission\s*Objectives?)\s*[:：]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObjectiveHeaderRegex();

    [GeneratedRegex(@"^(?:任务目标\s*)?\d+[\.\)、:：]\s*(?<text>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberedObjectiveRegex();
}

public sealed record MissionBriefingParsed(
    string? Title,
    string? Location,
    IReadOnlyList<string> Objectives,
    string Body,
    string RawFallback,
    bool IsStructured)
{
    public static MissionBriefingParsed Empty { get; } = new(
        Title: null,
        Location: null,
        Objectives: [],
        Body: string.Empty,
        RawFallback: string.Empty,
        IsStructured: false);
}
