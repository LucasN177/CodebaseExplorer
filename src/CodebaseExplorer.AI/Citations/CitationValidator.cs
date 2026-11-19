using System.Globalization;
using System.Text.RegularExpressions;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;

namespace CodebaseExplorer.AI.Citations;

/// <summary>
/// Finds and checks the citations of an answer (AI-005, AI_ARCHITECTURE.md §2 "Citation validator"): context keys
/// (<c>[F1]</c>, <c>[S2]</c>, also <c>[F1, S2]</c>) must belong to the manifest of what was sent; free-form
/// <c>path:start-end</c> citations must name a file of the model with lines inside it; elements must exist in the model.
/// Pure and deterministic; citations are returned once each, in order of first appearance.
/// </summary>
internal static partial class CitationValidator
{
    public const int MaxCitations = 200;

    public static IReadOnlyList<AiCitation> Validate(string answer, AiContextManifest manifest, ArchitectureModel model)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(model);

        var found = new List<(int Index, AiCitation Citation)>();
        foreach (Match match in KeyGroup().Matches(answer))
        {
            foreach (Capture key in match.Groups["key"].Captures)
            {
                found.Add((key.Index, CheckKey(key.Value.ToUpperInvariant(), manifest, model)));
            }
        }

        var files = model.Files.ToDictionary(f => f.Path.Value, StringComparer.Ordinal);
        var byName = model.Files.GroupBy(f => f.Path.FileName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.First() : null, StringComparer.Ordinal);
        foreach (Match match in PathCitation().Matches(answer))
        {
            if (CheckPath(match, files, byName) is { } citation)
            {
                found.Add((match.Index, citation));
            }
        }

        return [.. found
            .OrderBy(f => f.Index)
            .Select(f => f.Citation)
            .DistinctBy(c => c.Text, StringComparer.Ordinal)
            .Take(MaxCitations)];
    }

    private static AiCitation CheckKey(string key, AiContextManifest manifest, ArchitectureModel model)
    {
        var text = $"[{key}]";
        if (manifest.Find(key) is not { } entry)
        {
            return new AiCitation(text, key, null, null, null, null, false, "This key was not part of the context sent to the model.");
        }

        if (entry.ElementId is { } elementId && !ElementExists(model, elementId))
        {
            return new AiCitation(text, key, entry.Path, entry.StartLine, entry.EndLine, elementId, false, "The element is not part of the current model.");
        }

        return new AiCitation(text, key, entry.Path, entry.StartLine, entry.EndLine, entry.ElementId, true, null);
    }

    private static AiCitation? CheckPath(Match match, Dictionary<string, RepositoryFile> files, Dictionary<string, RepositoryFile?> byName)
    {
        var written = match.Groups["path"].Value.Replace('\\', '/').TrimStart('.', '/');
        var start = Line(match.Groups["start"].Value);
        var end = match.Groups["end"].Success ? Line(match.Groups["end"].Value) : start;
        var text = match.Value;

        var file = files.GetValueOrDefault(written)
            ?? (written.Contains('/', StringComparison.Ordinal) ? null : byName.GetValueOrDefault(written));
        if (file is null)
        {
            // Without a folder and without a matching file it is more likely "host:port" or a time than a citation.
            return written.Contains('/', StringComparison.Ordinal)
                ? new AiCitation(text, null, written, start, end, null, false, "The file is not part of the analyzed repository.")
                : null;
        }

        var path = file.Path.Value;
        if (start < 1 || end < start)
        {
            return new AiCitation(text, null, path, start, end, file.Id.ToString(), false, "The line range is not valid.");
        }

        if (file.LineCount is not { } lines)
        {
            return new AiCitation(text, null, path, start, end, file.Id.ToString(), false, "The file's line count is unknown.");
        }

        return end > lines
            ? new AiCitation(text, null, path, start, end, file.Id.ToString(), false,
                string.Create(CultureInfo.InvariantCulture, $"The file has only {lines} lines."))
            : new AiCitation(text, null, path, start, end, file.Id.ToString(), true, null);
    }

    private static bool ElementExists(ArchitectureModel model, string elementId)
    {
        if (elementId is not { Length: 32 } || !Guid.TryParseExact(elementId, "N", out var guid))
        {
            return false;
        }

        var id = new ElementId(guid);
        return model.FindProject(id) is not null || model.FindModule(id) is not null || model.FindEntity(id) is not null
            || model.FindExternalDependency(id) is not null || model.FindFile(id) is not null
            || model.Relationships.Any(r => r.Id == id);
    }

    private static int Line(string digits) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : -1;

    /// <summary><c>[F1]</c>, <c>[S12]</c>, <c>[F1, S2; F3]</c>.</summary>
    [GeneratedRegex(@"\[\s*(?<key>[FfSs]\d{1,4})(?:\s*[,;]\s*(?<key>[FfSs]\d{1,4}))*\s*\]")]
    private static partial Regex KeyGroup();

    /// <summary><c>src/A/B.cs:10</c> or <c>src/A/B.cs:10-20</c> (en dash allowed); the path has a file extension.</summary>
    [GeneratedRegex(@"(?<![\w/.\-])(?<path>(?:[\w.\-]+[/\\])*[\w\-][\w.\-]*\.[A-Za-z0-9]{1,12}):(?<start>\d{1,7})(?:\s?[-–]\s?(?<end>\d{1,7}))?(?!\d)")]
    private static partial Regex PathCitation();
}
