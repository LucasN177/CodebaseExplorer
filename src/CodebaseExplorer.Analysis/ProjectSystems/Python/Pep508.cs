namespace CodebaseExplorer.Analysis.ProjectSystems.Python;

/// <summary>A parsed PEP 508 requirement: distribution name, version specifier and environment marker.</summary>
internal sealed record Requirement(string Name, string? VersionSpec, string? Marker, bool IsDirectReference);

/// <summary>
/// Minimal PEP 508 requirement parser: <c>name[extras] (spec) ; marker</c> and <c>name @ url</c>. Only the parts the
/// model needs are extracted; specifiers and markers are kept as written (trimmed), never evaluated.
/// </summary>
internal static class Pep508
{
    public static Requirement? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var value = text.Trim();
        string? marker = null;
        var semicolon = value.IndexOf(';', StringComparison.Ordinal);
        if (semicolon >= 0)
        {
            marker = value[(semicolon + 1)..].Trim();
            value = value[..semicolon].Trim();
        }

        var end = 0;
        while (end < value.Length && (char.IsAsciiLetterOrDigit(value[end]) || value[end] is '.' or '_' or '-'))
        {
            end++;
        }

        if (end == 0 || !char.IsAsciiLetterOrDigit(value[0]) || !char.IsAsciiLetterOrDigit(value[end - 1]))
        {
            return null;
        }

        var name = value[..end];
        var rest = value[end..].TrimStart();
        if (rest.StartsWith('['))
        {
            var close = rest.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return null;
            }

            rest = rest[(close + 1)..].TrimStart();
        }

        if (rest.StartsWith('@'))
        {
            return new Requirement(name, null, NullIfEmpty(marker), IsDirectReference: true);
        }

        if (rest.StartsWith('(') && rest.EndsWith(')'))
        {
            rest = rest[1..^1].Trim();
        }

        if (rest.Length > 0 && !"<>=!~".Contains(rest[0], StringComparison.Ordinal))
        {
            return null; // not a version specifier
        }

        return new Requirement(name, NullIfEmpty(rest), NullIfEmpty(marker), IsDirectReference: false);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
