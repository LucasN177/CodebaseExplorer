namespace CodebaseExplorer.Analysis.ProjectSystems.Node;

/// <summary>
/// Reads the <c>packages</c> list of a <c>pnpm-workspace.yaml</c>. Decided in ANALYSIS-009: a minimal in-house
/// reader of the one YAML shape pnpm documents (a top-level <c>packages:</c> key followed by a block sequence of
/// plain or quoted scalars, or a flow sequence <c>[a, b]</c>) instead of a YAML library — the file needs nothing
/// else, and a general YAML parser would add aliases, anchors and tags as attack surface for no benefit.
/// Everything else in the file (catalogs, settings) is ignored.
/// </summary>
internal static class PnpmWorkspaceFile
{
    public static IReadOnlyList<PositionedValue> ReadPackages(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var result = new List<PositionedValue>();
        var lines = content.Split('\n');
        var inPackages = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripComment(lines[index].TrimEnd('\r'));
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var isTopLevel = !char.IsWhiteSpace(line[0]);
            if (isTopLevel)
            {
                inPackages = false;
                if (line.StartsWith("packages:", StringComparison.Ordinal))
                {
                    var rest = line["packages:".Length..].Trim();
                    if (rest.StartsWith('[') && rest.EndsWith(']'))
                    {
                        var column = line.IndexOf('[', StringComparison.Ordinal) + 2;
                        foreach (var item in rest[1..^1].Split(','))
                        {
                            AddItem(result, item, index + 1, column);
                        }
                    }
                    else
                    {
                        inPackages = rest.Length == 0;
                    }
                }

                continue;
            }

            var trimmed = line.TrimStart();
            if (inPackages && trimmed.StartsWith('-'))
            {
                AddItem(result, trimmed[1..], index + 1, line.Length - trimmed.Length + 1);
            }
        }

        return result;
    }

    private static void AddItem(List<PositionedValue> result, string raw, int line, int column)
    {
        var value = raw.Trim();
        if (value.Length >= 2 && (value[0] == '\'' || value[0] == '"') && value[^1] == value[0])
        {
            value = value[1..^1];
        }

        if (value.Length > 0)
        {
            result.Add(new PositionedValue(value, line, column));
        }
    }

    /// <summary>Removes a <c>#</c> comment that is not inside quotes.</summary>
    private static string StripComment(string line)
    {
        char? quote = null;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote is null && (c == '\'' || c == '"'))
            {
                quote = c;
            }
            else if (c == quote)
            {
                quote = null;
            }
            else if (quote is null && c == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                return line[..i];
            }
        }

        return line;
    }
}
