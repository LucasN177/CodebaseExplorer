using System.Text.RegularExpressions;
using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Parsing;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>
/// Static parsers for <c>.sln</c> (text) and <c>.slnx</c> (XML) solution files. Solution folders are
/// normalized to the <c>.slnx</c> form (<c>/src/</c>, <c>/src/sub/</c>) so both formats yield
/// identical memberships. Only entries whose path ends in a <c>*proj</c> extension are projects.
/// </summary>
internal static partial class SolutionParser
{
    private const string SolutionFolderTypeGuid = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    /// <summary>Parses by extension (<c>.slnx</c> as XML, everything else as text).</summary>
    public static SolutionDocument Parse(RelativePath solutionPath, ElementId solutionFileId, string content, long maxCharacters) =>
        solutionPath.FileName.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnx(solutionPath, solutionFileId, content, maxCharacters)
            : ParseSln(solutionPath, solutionFileId, content);

    public static SolutionDocument ParseSln(RelativePath solutionPath, ElementId solutionFileId, string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var diagnostics = new List<AnalysisDiagnostic>();
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        if (!lines.Any(l => l.TrimStart('﻿', ' ', '\t').StartsWith("Microsoft Visual Studio Solution File", StringComparison.Ordinal)))
        {
            diagnostics.Add(Warning(DotNetCodes.SolutionNotRecognized, $"{solutionPath}: not a Visual Studio solution file (header missing).", solutionFileId, 1, 1));
            return new SolutionDocument(solutionPath, [], diagnostics);
        }

        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // guid -> name
        var projects = new List<(string Guid, string Path, int Line, int Column)>();
        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // child guid -> parent guid
        var inNestedProjects = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            var lineNumber = index + 1;

            if (trimmed.StartsWith("Project(", StringComparison.Ordinal))
            {
                var match = ProjectLine().Match(trimmed);
                if (!match.Success)
                {
                    diagnostics.Add(Warning(DotNetCodes.SolutionMalformedEntry,
                        $"{solutionPath}:{lineNumber}: project entry could not be parsed.", solutionFileId, lineNumber, Column(line)));
                    continue;
                }

                var type = match.Groups["type"].Value;
                var guid = match.Groups["guid"].Value;
                if (string.Equals(type, SolutionFolderTypeGuid, StringComparison.OrdinalIgnoreCase))
                {
                    folders[guid] = match.Groups["name"].Value;
                }
                else
                {
                    projects.Add((guid, match.Groups["path"].Value, lineNumber, Column(line)));
                }

                continue;
            }

            if (trimmed.StartsWith("GlobalSection(NestedProjects)", StringComparison.Ordinal))
            {
                inNestedProjects = true;
                continue;
            }

            if (inNestedProjects)
            {
                if (trimmed.StartsWith("EndGlobalSection", StringComparison.Ordinal))
                {
                    inNestedProjects = false;
                    continue;
                }

                var nested = NestedLine().Match(trimmed);
                if (nested.Success)
                {
                    parents[nested.Groups["child"].Value] = nested.Groups["parent"].Value;
                }
            }
        }

        var directory = RepositoryPaths.DirectoryOf(solutionPath);
        var entries = new List<SolutionProjectEntry>();
        foreach (var (guid, path, lineNumber, column) in projects)
        {
            if (!IsProjectFile(path))
            {
                continue;
            }

            var resolved = RepositoryPaths.Resolve(directory, path);
            if (resolved is not { IsRoot: false } projectPath)
            {
                diagnostics.Add(Warning(DotNetCodes.PathOutsideRepository,
                    $"{solutionPath}:{lineNumber}: project path '{path}' lies outside the repository.", solutionFileId, lineNumber, column));
                continue;
            }

            entries.Add(new SolutionProjectEntry(projectPath, FolderPath(guid, folders, parents), lineNumber, column));
        }

        return new SolutionDocument(solutionPath, entries, diagnostics);
    }

    public static SolutionDocument ParseSlnx(RelativePath solutionPath, ElementId solutionFileId, string content, long maxCharacters)
    {
        var diagnostics = new List<AnalysisDiagnostic>();

        if (!SafeXml.TryLoad(content, maxCharacters, out var document, out var error) || document!.Root is null)
        {
            diagnostics.Add(Warning(DotNetCodes.InvalidXml, $"{solutionPath}: invalid XML ({error ?? "no root element"}).", solutionFileId, 1, 1));
            return new SolutionDocument(solutionPath, [], diagnostics);
        }

        if (document.Root.Name.LocalName != "Solution")
        {
            diagnostics.Add(Warning(DotNetCodes.SolutionNotRecognized, $"{solutionPath}: root element is not <Solution>.", solutionFileId, 1, 1));
            return new SolutionDocument(solutionPath, [], diagnostics);
        }

        var directory = RepositoryPaths.DirectoryOf(solutionPath);
        var entries = new List<SolutionProjectEntry>();
        foreach (var project in document.Root.Descendants().Where(e => e.Name.LocalName == "Project"))
        {
            var (line, column) = SafeXml.PositionOf(project);
            var path = (string?)project.Attribute("Path");
            if (string.IsNullOrWhiteSpace(path))
            {
                diagnostics.Add(Warning(DotNetCodes.SolutionMalformedEntry,
                    $"{solutionPath}:{line}: <Project> without Path.", solutionFileId, Math.Max(1, line), column));
                continue;
            }

            if (!IsProjectFile(path))
            {
                continue;
            }

            var resolved = RepositoryPaths.Resolve(directory, path);
            if (resolved is not { IsRoot: false } projectPath)
            {
                diagnostics.Add(Warning(DotNetCodes.PathOutsideRepository,
                    $"{solutionPath}:{line}: project path '{path}' lies outside the repository.", solutionFileId, Math.Max(1, line), column));
                continue;
            }

            var folder = project.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Folder");
            entries.Add(new SolutionProjectEntry(projectPath, NormalizeFolder((string?)folder?.Attribute("Name")), line, column));
        }

        return new SolutionDocument(solutionPath, entries, diagnostics);
    }

    private static bool IsProjectFile(string path) =>
        ProjectExtension().IsMatch(path.Trim());

    private static string? FolderPath(string guid, Dictionary<string, string> folders, Dictionary<string, string> parents)
    {
        var names = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = guid;

        while (parents.TryGetValue(current, out var parent) && folders.TryGetValue(parent, out var name) && visited.Add(parent))
        {
            names.Insert(0, name);
            current = parent;
        }

        return names.Count == 0 ? null : "/" + string.Join('/', names) + "/";
    }

    private static string? NormalizeFolder(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim().Trim('/');
        return trimmed.Length == 0 ? null : "/" + trimmed + "/";
    }

    private static int Column(string line) => line.Length - line.TrimStart().Length + 1;

    private static AnalysisDiagnostic Warning(string code, string message, ElementId fileId, int line, int column) =>
        new(DiagnosticSeverity.Warning, code, message, DotNetCodes.Analyzer, SourceLocation.Line(fileId, Math.Max(1, line), Math.Max(1, column)));

    [GeneratedRegex("""^Project\("\{(?<type>[0-9A-Fa-f-]{36})\}"\)\s*=\s*"(?<name>[^"]*)"\s*,\s*"(?<path>[^"]*)"\s*,\s*"\{(?<guid>[0-9A-Fa-f-]{36})\}"\s*$""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProjectLine();

    [GeneratedRegex("""^\{(?<child>[0-9A-Fa-f-]{36})\}\s*=\s*\{(?<parent>[0-9A-Fa-f-]{36})\}$""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NestedLine();

    [GeneratedRegex(@"\.[A-Za-z]+proj$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProjectExtension();
}
