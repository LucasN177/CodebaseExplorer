using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.ProjectSystems.Python;

/// <summary>A declared dependency with the rule and location that declares it.</summary>
internal sealed record PythonDependency(
    string Name,
    string Rule,
    RelativePath File,
    int Line,
    int Column,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>A problem found while reading a manifest (becomes a diagnostic of the analyzer).</summary>
internal sealed record ManifestProblem(DiagnosticSeverity Severity, string Code, string Message, RelativePath File, int Line, int Column);

/// <summary>What one Python manifest contributes to its project (later manifests only fill gaps).</summary>
internal sealed class PythonManifestFacts
{
    public string? Name { get; set; }

    public string? Version { get; set; }

    public string? RequiresPython { get; set; }

    public string? BuildBackend { get; set; }

    public List<PythonDependency> Dependencies { get; } = [];

    public List<ManifestProblem> Problems { get; } = [];

    /// <summary>Adds a dependency from a PEP 508 requirement string; invalid or unsupported ones become problems.</summary>
    public void AddRequirement(string text, string rule, RelativePath file, int line, int column, IReadOnlyDictionary<string, string>? extraAttributes = null)
    {
        var requirement = Pep508.Parse(text);
        if (requirement is null)
        {
            Problems.Add(new ManifestProblem(DiagnosticSeverity.Info, PythonCodes.RequirementInvalid,
                $"{file}:{line}: '{Shorten(text)}' is not a requirement this analyzer understands; ignored.", file, line, column));
            return;
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (requirement.VersionSpec is { } spec)
        {
            attributes[PythonCodes.AttributeVersionSpec] = spec;
        }

        if (requirement.Marker is { } marker)
        {
            attributes[PythonCodes.AttributeMarker] = marker;
        }

        foreach (var (key, value) in extraAttributes ?? new Dictionary<string, string>())
        {
            attributes[key] = value;
        }

        Dependencies.Add(new PythonDependency(requirement.Name, rule, file, line, column, attributes));
    }

    public static string Shorten(string text) => text.Length <= 80 ? text : text[..80] + "…";
}
