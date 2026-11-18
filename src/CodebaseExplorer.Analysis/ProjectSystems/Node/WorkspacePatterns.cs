using System.Text;
using System.Text.RegularExpressions;

namespace CodebaseExplorer.Analysis.ProjectSystems.Node;

/// <summary>
/// Workspace member patterns of npm/yarn (<c>workspaces</c>) and pnpm (<c>pnpm-workspace.yaml</c>): globs over
/// folder paths relative to the workspace root, <c>*</c> within one segment, <c>**</c> across segments, a
/// leading <c>!</c> excludes. Matching is ordinal (case-sensitive) and never touches the file system.
/// </summary>
internal sealed class WorkspacePatterns
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly List<Regex> _includes = [];
    private readonly List<Regex> _excludes = [];

    public WorkspacePatterns(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        foreach (var raw in patterns)
        {
            var pattern = raw.Trim();
            var exclude = pattern.StartsWith('!');
            pattern = pattern.TrimStart('!').Replace('\\', '/').Trim('/');
            while (pattern.StartsWith("./", StringComparison.Ordinal))
            {
                pattern = pattern[2..];
            }

            if (pattern.Length == 0)
            {
                continue;
            }

            (exclude ? _excludes : _includes).Add(ToRegex(pattern));
        }
    }

    /// <summary>True if the folder (relative to the workspace root, <c>/</c>-separated) is a workspace member.</summary>
    public bool Matches(string relativeFolder) =>
        relativeFolder.Length > 0
        && _includes.Any(r => r.IsMatch(relativeFolder))
        && !_excludes.Any(r => r.IsMatch(relativeFolder));

    private static Regex ToRegex(string glob)
    {
        var builder = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                // "**/" matches zero or more folders; a trailing "**" any remainder.
                var followedBySlash = i + 2 < glob.Length && glob[i + 2] == '/';
                builder.Append(followedBySlash ? "(?:[^/]+/)*" : ".*");
                i += followedBySlash ? 2 : 1;
            }
            else if (c == '*')
            {
                builder.Append("[^/]*");
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        return new Regex(builder.Append('$').ToString(), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, MatchTimeout);
    }
}
