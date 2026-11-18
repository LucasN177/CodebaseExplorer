using CodebaseExplorer.Analysis.Parsing;

namespace CodebaseExplorer.Analysis.ProjectSystems.Node;

/// <summary>
/// The parts of one <c>tsconfig.json</c> (JSONC) that Codebase Explorer uses: project <c>references</c>,
/// <c>extends</c>, <c>compilerOptions.baseUrl</c> and <c>compilerOptions.paths</c>. Paths are kept as written,
/// relative to the file's folder; resolution (including the <c>extends</c> chain) is the caller's job.
/// </summary>
internal sealed record TsConfigFile
{
    public IReadOnlyList<PositionedValue> References { get; init; } = [];

    /// <summary>
    /// A "solution-style" config (<c>"files": []</c> without <c>include</c>) only groups other projects; its
    /// references are not dependencies (ts-monorepo-workspaces EXPECTED.md §3).
    /// </summary>
    public bool IsSolutionStyle { get; init; }

    /// <summary>The first <c>extends</c> entry that is a relative path (package configs are not resolved).</summary>
    public string? Extends { get; init; }

    public string? BaseUrl { get; init; }

    /// <summary><c>compilerOptions.paths</c>: pattern → substitutions, or <see langword="null"/> if not set here.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Paths { get; init; }

    public static bool TryParse(string content, out TsConfigFile? config, out string? error)
    {
        config = null;
        if (!SafeJson.TryParse(content, out var value, out error))
        {
            return false;
        }

        if (value is not JsonObject root)
        {
            error = "The file is not a JSON object.";
            return false;
        }

        var options = root["compilerOptions"] as JsonObject;
        Dictionary<string, IReadOnlyList<string>>? paths = null;
        if (options?["paths"] is JsonObject pathMap)
        {
            paths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var entry in pathMap.Properties)
            {
                if (entry.Value is JsonArray targets)
                {
                    paths[entry.Name] = [.. targets.Items.OfType<JsonString>().Select(t => t.Value)];
                }
            }
        }

        var extends = root["extends"] switch
        {
            JsonString single => [single.Value],
            JsonArray many => many.Items.OfType<JsonString>().Select(s => s.Value).ToArray(),
            _ => [],
        };

        config = new TsConfigFile
        {
            References = root["references"] is JsonArray references
                ? [.. references.Items.OfType<JsonObject>()
                    .Select(r => r.Property("path"))
                    .Where(p => p?.Value is JsonString)
                    .Select(p => new PositionedValue(((JsonString)p!.Value).Value, p.Line, p.Column))]
                : [],
            IsSolutionStyle = root["files"] is JsonArray { Items.Count: 0 } && root["include"] is null,
            Extends = extends.FirstOrDefault(e => e.StartsWith("./", StringComparison.Ordinal) || e.StartsWith("../", StringComparison.Ordinal)),
            BaseUrl = options?.String("baseUrl"),
            Paths = paths,
        };
        return true;
    }
}
