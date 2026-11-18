using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;
using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace CodebaseExplorer.Analysis.ProjectSystems.Python;

/// <summary>
/// Reads <c>pyproject.toml</c> with Tomlyn's syntax parser (positions, nesting depth limited, no evaluation):
/// PEP 621 <c>[project]</c>, PEP 735 <c>[dependency-groups]</c>, Poetry <c>[tool.poetry]</c> and
/// <c>[build-system] build-backend</c>. <c>[build-system] requires</c> are build requirements, not dependencies.
/// </summary>
internal static class PyProjectFile
{
    private const int MaxDepth = 32;

    private static readonly TomlSerializerOptions Options = new() { MaxDepth = MaxDepth };

    public static PythonManifestFacts Read(RelativePath file, string content)
    {
        var facts = new PythonManifestFacts();
        DocumentSyntax document;
        try
        {
            document = SyntaxParser.Parse(content, Options, file.Value, validate: true);
        }
        catch (TomlException exception) // e.g. the nesting depth limit
        {
            facts.Problems.Add(new ManifestProblem(DiagnosticSeverity.Warning, PythonCodes.ManifestInvalid, $"{file}: {exception.Message}", file, 1, 1));
            return facts;
        }

        if (document.HasErrors)
        {
            var first = document.Diagnostics.FirstOrDefault();
            facts.Problems.Add(new ManifestProblem(DiagnosticSeverity.Warning, PythonCodes.ManifestInvalid,
                $"{file}: invalid TOML: {first}", file, 1, 1));
            return facts;
        }

        foreach (var (path, entry) in Entries(document))
        {
            var key = string.Join('.', path);
            switch (key)
            {
                case "project.name":
                    facts.Name ??= StringOf(entry.Value);
                    break;
                case "project.version":
                    facts.Version ??= StringOf(entry.Value);
                    break;
                case "project.requires-python":
                    facts.RequiresPython ??= StringOf(entry.Value);
                    break;
                case "build-system.build-backend":
                    facts.BuildBackend ??= StringOf(entry.Value);
                    break;
                case "tool.poetry.name":
                    facts.Name ??= StringOf(entry.Value);
                    break;
                case "tool.poetry.version":
                    facts.Version ??= StringOf(entry.Value);
                    break;
                case "project.dependencies":
                    AddArray(facts, file, entry.Value, PythonCodes.RuleProjectDependencies, null);
                    break;
                default:
                    AddGrouped(facts, file, path, entry);
                    break;
            }
        }

        return facts;
    }

    private static void AddGrouped(PythonManifestFacts facts, RelativePath file, string[] path, KeyValueSyntax entry)
    {
        // [project.optional-dependencies] extra = [...]
        if (path is ["project", "optional-dependencies", var extra])
        {
            AddArray(facts, file, entry.Value, PythonCodes.RuleOptionalDependencies, new Dictionary<string, string> { [PythonCodes.AttributeExtra] = extra });
        }

        // [dependency-groups] group = [...]
        else if (path is ["dependency-groups", var group])
        {
            AddArray(facts, file, entry.Value, PythonCodes.RuleDependencyGroups, new Dictionary<string, string> { [PythonCodes.AttributeGroup] = group });
        }

        // [tool.poetry.dependencies] name = "spec" | { version = "spec", … }
        else if (path is ["tool", "poetry", "dependencies", var name])
        {
            if (name == "python")
            {
                facts.RequiresPython ??= StringOf(entry.Value);
            }
            else
            {
                AddPoetry(facts, file, name, entry, PythonCodes.RulePoetryDependencies, []);
            }
        }
        else if (path is ["tool", "poetry", "dev-dependencies", var devName])
        {
            AddPoetry(facts, file, devName, entry, PythonCodes.RulePoetryDevDependencies, new Dictionary<string, string> { [PythonCodes.AttributeDevOnly] = "true" });
        }
        else if (path is ["tool", "poetry", "group", var poetryGroup, "dependencies", var groupName])
        {
            var attributes = new Dictionary<string, string> { [PythonCodes.AttributeGroup] = poetryGroup };
            if (poetryGroup != "main")
            {
                attributes[PythonCodes.AttributeDevOnly] = "true";
            }

            AddPoetry(facts, file, groupName, entry, PythonCodes.RulePoetryGroupDependencies, attributes);
        }
    }

    private static void AddArray(PythonManifestFacts facts, RelativePath file, ValueSyntax? value, string rule, Dictionary<string, string>? attributes)
    {
        if (value is not ArraySyntax array)
        {
            return;
        }

        foreach (var item in array.Items)
        {
            if (item.Value is StringValueSyntax requirement && requirement.Value is { } text)
            {
                facts.AddRequirement(text, rule, file, requirement.Span.Start.Line + 1, requirement.Span.Start.Column + 1, attributes);
            }

            // Tables in dependency groups ({ include-group = "…" }) are references to other groups, not packages.
        }
    }

    private static void AddPoetry(PythonManifestFacts facts, RelativePath file, string name, KeyValueSyntax entry, string rule, Dictionary<string, string> attributes)
    {
        var spec = entry.Value switch
        {
            StringValueSyntax s => s.Value,
            InlineTableSyntax table => table.Items.Select(i => i.KeyValue).Where(kv => KeyText(kv?.Key) is ["version"]).Select(kv => StringOf(kv!.Value)).FirstOrDefault(),
            _ => null,
        };

        var all = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(spec) && spec.Trim() != "*")
        {
            all[PythonCodes.AttributeVersionSpec] = spec.Trim();
        }

        var position = entry.Key!.Span.Start;
        if (Pep508.Parse(name) is { VersionSpec: null, Marker: null } requirement && requirement.Name == name)
        {
            facts.Dependencies.Add(new PythonDependency(name, rule, file, position.Line + 1, position.Column + 1, all));
        }
        else
        {
            facts.Problems.Add(new ManifestProblem(DiagnosticSeverity.Info, PythonCodes.RequirementInvalid,
                $"{file}:{position.Line + 1}: '{PythonManifestFacts.Shorten(name)}' is not a valid package name; ignored.", file, position.Line + 1, position.Column + 1));
        }
    }

    /// <summary>Every key/value with its full dotted path (table name + dotted key), in document order.</summary>
    private static IEnumerable<(string[] Path, KeyValueSyntax Entry)> Entries(DocumentSyntax document)
    {
        foreach (var entry in document.KeyValues)
        {
            yield return ([.. KeyText(entry.Key)], entry);
        }

        foreach (var table in document.Tables)
        {
            if (table is TableArraySyntax)
            {
                continue; // arrays of tables carry no facts used here
            }

            var prefix = KeyText(table.Name);
            foreach (var item in table.Items)
            {
                yield return ([.. prefix, .. KeyText(item.Key)], item);
            }
        }
    }

    private static string[] KeyText(KeySyntax? key)
    {
        if (key?.Key is null)
        {
            return [];
        }

        var parts = new List<string> { PartText(key.Key) };
        parts.AddRange(key.DotKeys.Select(d => PartText(d.Key!)));
        return [.. parts];
    }

    private static string PartText(BareKeyOrStringValueSyntax part) =>
        part switch
        {
            StringValueSyntax quoted => quoted.Value ?? string.Empty,
            BareKeySyntax bare => bare.Key?.Text ?? string.Empty,
            _ => string.Empty,
        };

    private static string? StringOf(ValueSyntax? value) => value is StringValueSyntax s && !string.IsNullOrWhiteSpace(s.Value) ? s.Value.Trim() : null;
}
