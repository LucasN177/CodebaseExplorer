using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.ProjectSystems.Python;

/// <summary>An <c>-r</c>/<c>--requirement</c> include of a requirements file, as written.</summary>
internal sealed record RequirementsInclude(string Path, int Line);

/// <summary>
/// Reads a pip requirements file line by line: requirements (PEP 508), comments, line continuations, and options.
/// Includes are returned for the caller to resolve inside the repository; every other option (<c>-e</c>,
/// <c>-c</c>, <c>--index-url</c>, …) and URL/VCS requirements are reported and ignored — nothing is fetched or run.
/// </summary>
internal static class RequirementsFile
{
    public static PythonManifestFacts Read(RelativePath file, string content, List<RequirementsInclude> includes)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(includes);

        var facts = new PythonManifestFacts();
        var lines = content.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index].TrimEnd('\r');

            // Joined continuation lines ("\" at the end) report the first line.
            while (line.EndsWith('\\') && index + 1 < lines.Length)
            {
                line = line[..^1] + lines[++index].TrimEnd('\r');
            }

            line = StripComment(line).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('-'))
            {
                var (option, argument) = SplitOption(line);
                if (option is "-r" or "--requirement" && argument.Length > 0)
                {
                    includes.Add(new RequirementsInclude(argument, lineNumber));
                }
                else
                {
                    facts.Problems.Add(new ManifestProblem(DiagnosticSeverity.Info, PythonCodes.OptionIgnored,
                        $"{file}:{lineNumber}: option '{option}' is ignored (nothing is installed or fetched).", file, lineNumber, 1));
                }

                continue;
            }

            if (line.Contains("://", StringComparison.Ordinal) && !line.Contains(" @ ", StringComparison.Ordinal))
            {
                facts.Problems.Add(new ManifestProblem(DiagnosticSeverity.Info, PythonCodes.RequirementUnsupported,
                    $"{file}:{lineNumber}: URL or VCS requirements without a name are ignored.", file, lineNumber, 1));
                continue;
            }

            facts.AddRequirement(line, PythonCodes.RuleRequirements, file, lineNumber, 1);
        }

        return facts;
    }

    private static (string Option, string Argument) SplitOption(string line)
    {
        var separator = line.IndexOfAny([' ', '\t', '=']);
        return separator < 0 ? (line, string.Empty) : (line[..separator], line[(separator + 1)..].Trim());
    }

    /// <summary>pip comments start with <c>#</c> at the line start or after whitespace.</summary>
    private static string StripComment(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                return line[..i];
            }
        }

        return line;
    }
}
