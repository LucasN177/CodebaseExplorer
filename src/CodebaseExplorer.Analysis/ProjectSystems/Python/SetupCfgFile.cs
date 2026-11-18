using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.ProjectSystems.Python;

/// <summary>
/// Reads the setuptools declarative configuration <c>setup.cfg</c> (INI): <c>[metadata] name/version</c>,
/// <c>[options] python_requires/install_requires</c> and <c>[options.extras_require]</c>. A minimal in-house INI
/// reader (sections, <c>key = value</c> with indented continuation lines, <c>#</c>/<c>;</c> comments); no
/// interpolation, no includes.
/// </summary>
internal static class SetupCfgFile
{
    public static PythonManifestFacts Read(RelativePath file, string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var facts = new PythonManifestFacts();
        foreach (var (section, key, values) in Entries(content))
        {
            switch (section, key)
            {
                case ("metadata", "name"):
                    facts.Name ??= FirstText(values);
                    break;
                case ("metadata", "version"):
                    facts.Version ??= FirstText(values);
                    break;
                case ("options", "python_requires"):
                    facts.RequiresPython ??= FirstText(values);
                    break;
                case ("options", "install_requires"):
                    foreach (var (text, line, column) in values)
                    {
                        facts.AddRequirement(text, PythonCodes.RuleSetupCfgInstallRequires, file, line, column);
                    }

                    break;
                case ("options.extras_require", _):
                    foreach (var (text, line, column) in values)
                    {
                        facts.AddRequirement(text, PythonCodes.RuleSetupCfgExtrasRequire, file, line, column,
                            new Dictionary<string, string> { [PythonCodes.AttributeExtra] = key });
                    }

                    break;
            }
        }

        return facts;
    }

    private static string? FirstText(List<(string Text, int Line, int Column)> values) => values.Count > 0 ? values[0].Text : null;

    /// <summary>Each key with its non-empty value lines (the inline value and indented continuation lines).</summary>
    private static IEnumerable<(string Section, string Key, List<(string Text, int Line, int Column)> Values)> Entries(string content)
    {
        var lines = content.Split('\n');
        var section = string.Empty;
        string? key = null;
        var values = new List<(string, int, int)>();

        for (var index = 0; index < lines.Length; index++)
        {
            var raw = lines[index].TrimEnd('\r');
            var trimmed = raw.Trim();
            var isContinuation = raw.Length > 0 && char.IsWhiteSpace(raw[0]) && key is not null;

            if (trimmed.Length == 0 || trimmed[0] is '#' or ';')
            {
                continue;
            }

            if (isContinuation)
            {
                values.Add((trimmed, index + 1, raw.Length - raw.TrimStart().Length + 1));
                continue;
            }

            if (key is not null)
            {
                yield return (section, key, values);
                key = null;
                values = [];
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                continue;
            }

            var separator = trimmed.IndexOfAny(['=', ':']);
            if (separator <= 0)
            {
                continue;
            }

            key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();
            if (value.Length > 0)
            {
                values.Add((value, index + 1, raw.IndexOf(value, StringComparison.Ordinal) + 1));
            }
        }

        if (key is not null)
        {
            yield return (section, key, values);
        }
    }
}
