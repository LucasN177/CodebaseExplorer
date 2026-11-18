using CodebaseExplorer.Analysis.Parsing;

namespace CodebaseExplorer.Analysis.ProjectSystems.Node;

/// <summary>A dependency entry of <c>package.json</c> with the position of its property name.</summary>
internal sealed record PackageDependency(string Name, string VersionSpec, string DependencyType, int Line, int Column)
{
    public bool IsDevOnly => DependencyType == PackageManifest.DevDependencies;
}

/// <summary>A named entry (script, workspace pattern) with its position.</summary>
internal sealed record PositionedValue(string Value, int Line, int Column);

/// <summary>
/// The facts Codebase Explorer reads from a <c>package.json</c> (ANALYSIS_ENGINE.md Stage 2, Node). Scripts are
/// only listed, never run.
/// </summary>
internal sealed record PackageManifest
{
    public const string Dependencies = "dependencies";
    public const string DevDependencies = "devDependencies";
    public const string PeerDependencies = "peerDependencies";
    public const string OptionalDependencies = "optionalDependencies";

    private static readonly string[] DependencyTypes = [Dependencies, DevDependencies, PeerDependencies, OptionalDependencies];

    /// <summary>npm lifecycle scripts that a package manager runs automatically (npm docs, "scripts").</summary>
    public static readonly IReadOnlySet<string> LifecycleScripts = new HashSet<string>(StringComparer.Ordinal)
    {
        "preinstall", "install", "postinstall", "preuninstall", "uninstall", "postuninstall",
        "prepublish", "preprepare", "prepare", "postprepare", "prepack", "postpack",
        "prepublishOnly", "publish", "postpublish", "dependencies",
    };

    public string? Name { get; init; }

    public string? Version { get; init; }

    public bool IsPrivate { get; init; }

    /// <summary>Workspace patterns (<c>workspaces</c> as array or <c>workspaces.packages</c>), or <see langword="null"/>.</summary>
    public IReadOnlyList<PositionedValue>? Workspaces { get; init; }

    public IReadOnlyList<PositionedValue> Scripts { get; init; } = [];

    public IReadOnlyList<PackageDependency> PackageDependencies { get; init; } = [];

    /// <summary>Candidate entry points in resolution order: <c>exports["."]</c> (types, import, require, default), <c>types</c>, <c>typings</c>, <c>module</c>, <c>main</c>.</summary>
    public IReadOnlyList<string> EntryPoints { get; init; } = [];

    /// <summary>Reads a parsed <c>package.json</c>; non-conforming entries are skipped and returned as messages.</summary>
    public static PackageManifest From(JsonObject root, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(problems);

        var dependencies = new List<PackageDependency>();
        foreach (var type in DependencyTypes)
        {
            if (root[type] is not { } section)
            {
                continue;
            }

            if (section is not JsonObject entries)
            {
                problems.Add($"'{type}' is not an object.");
                continue;
            }

            foreach (var entry in entries.Properties)
            {
                if (entry.Value is JsonString spec && !string.IsNullOrWhiteSpace(entry.Name))
                {
                    dependencies.Add(new PackageDependency(entry.Name.Trim(), spec.Value.Trim(), type, entry.Line, entry.Column));
                }
                else
                {
                    problems.Add($"'{type}.{entry.Name}' has no version string.");
                }
            }
        }

        return new PackageManifest
        {
            Name = NonEmpty(root.String("name")),
            Version = NonEmpty(root.String("version")),
            IsPrivate = root["private"] is JsonLiteral { Raw: "true" },
            Workspaces = WorkspacesOf(root["workspaces"]),
            Scripts = root["scripts"] is JsonObject scripts
                ? [.. scripts.Properties.Select(p => new PositionedValue(p.Name, p.Line, p.Column))]
                : [],
            PackageDependencies = dependencies,
            EntryPoints = EntryPointsOf(root),
        };
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<PositionedValue>? WorkspacesOf(JsonValue? value)
    {
        var patterns = value switch
        {
            JsonArray array => array,
            JsonObject { } yarn when yarn["packages"] is JsonArray packages => packages,
            _ => null,
        };

        return patterns?.Items.OfType<JsonString>()
            .Where(s => !string.IsNullOrWhiteSpace(s.Value))
            .Select(s => new PositionedValue(s.Value.Trim(), s.Line, s.Column))
            .ToList();
    }

    private static List<string> EntryPointsOf(JsonObject root)
    {
        var result = new List<string>();
        var exports = root["exports"];
        if (exports is JsonObject map && map["."] is { } dot)
        {
            exports = dot;
        }

        switch (exports)
        {
            case JsonString single:
                result.Add(single.Value);
                break;
            case JsonObject conditions when !conditions.Properties.Any(p => p.Name.StartsWith('.')):
                foreach (var condition in new[] { "types", "import", "require", "default" })
                {
                    if (conditions[condition] is JsonString target)
                    {
                        result.Add(target.Value);
                    }
                }

                break;
        }

        foreach (var field in new[] { "types", "typings", "module", "main" })
        {
            if (root.String(field) is { } target)
            {
                result.Add(target);
            }
        }

        return [.. result.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.Ordinal)];
    }
}
