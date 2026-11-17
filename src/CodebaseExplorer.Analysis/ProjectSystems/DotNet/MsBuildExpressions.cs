using System.Text;
using System.Text.RegularExpressions;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>
/// The small, safe subset of MSBuild expressions supported by static project analysis
/// (ANALYSIS_ENGINE.md Stage 2): <c>$(Name)</c> substitution with known literal values and
/// conditions of the form <c>'…' == '…'</c> / <c>'…' != '…'</c>. Property functions <c>$([…])</c>,
/// item lists <c>@(…)</c> and metadata <c>%(…)</c> are never evaluated.
/// </summary>
internal static partial class MsBuildExpressions
{
    /// <summary>
    /// Substitutes <c>$(Name)</c> references. Returns <see langword="null"/> if the value contains a
    /// property function, item list or metadata reference, or a property that is not known;
    /// <paramref name="problem"/> then describes why.
    /// </summary>
    public static string? Expand(string value, Func<string, string?> property, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(property);

        problem = null;
        if (value.Contains("$([", StringComparison.Ordinal))
        {
            problem = "property functions are not evaluated";
            return null;
        }

        if (value.Contains("@(", StringComparison.Ordinal) || value.Contains("%(", StringComparison.Ordinal))
        {
            problem = "item lists and metadata are not evaluated";
            return null;
        }

        var builder = new StringBuilder();
        var last = 0;
        foreach (Match match in PropertyReference().Matches(value))
        {
            var name = match.Groups["name"].Value;
            var resolved = property(name);
            if (resolved is null)
            {
                problem = $"property '{name}' is not known statically";
                return null;
            }

            builder.Append(value, last, match.Index - last).Append(resolved);
            last = match.Index + match.Length;
        }

        if (value.IndexOf("$(", last, StringComparison.Ordinal) >= 0)
        {
            problem = "malformed property reference";
            return null;
        }

        return builder.Append(value, last, value.Length - last).ToString();
    }

    /// <summary>
    /// Evaluates <c>'left' == 'right'</c> or <c>'left' != 'right'</c> (operands quoted, compared
    /// case-insensitively like MSBuild). Empty or whitespace conditions are true. Everything else —
    /// <c>and</c>/<c>or</c>, functions such as <c>Exists()</c>, numeric comparisons — is unknown.
    /// </summary>
    public static ConditionResult EvaluateCondition(string? condition, Func<string, string?> property, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(condition))
        {
            return ConditionResult.True;
        }

        var match = Comparison().Match(condition);
        if (!match.Success)
        {
            problem = "only '…' == '…' and '…' != '…' conditions are supported";
            return ConditionResult.Unknown;
        }

        var left = Expand(match.Groups["left"].Value, property, out problem);
        var right = Expand(match.Groups["right"].Value, property, out var rightProblem);
        if (left is null || right is null)
        {
            problem ??= rightProblem;
            return ConditionResult.Unknown;
        }

        var equal = string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        return (match.Groups["op"].Value == "==") == equal ? ConditionResult.True : ConditionResult.False;
    }

    [GeneratedRegex(@"\$\((?<name>[A-Za-z_][A-Za-z0-9_.\-]*)\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PropertyReference();

    [GeneratedRegex(@"^\s*'(?<left>[^']*)'\s*(?<op>==|!=)\s*'(?<right>[^']*)'\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Comparison();
}
