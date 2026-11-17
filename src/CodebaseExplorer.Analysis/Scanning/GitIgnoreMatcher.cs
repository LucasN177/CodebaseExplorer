namespace CodebaseExplorer.Analysis.Scanning;

/// <summary>
/// Evaluates <c>.gitignore</c> rules of a repository. Rules of outer files are added before rules of
/// nested files, and the last matching rule decides (negated rules re-include), which gives nested
/// files precedence as in Git. Re-including a file inside an ignored directory is impossible because
/// the scanner never descends into ignored directories — also as in Git.
/// </summary>
internal sealed class GitIgnoreMatcher
{
    private readonly List<GitIgnoreRule> _rules = [];

    public int RuleCount => _rules.Count;

    /// <summary>Adds the rules of a <c>.gitignore</c> located in <paramref name="baseDirectory"/>.</summary>
    public void AddFile(string baseDirectory, string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        foreach (var line in content.Split('\n'))
        {
            if (GitIgnoreRule.Parse(line, baseDirectory) is { } rule)
            {
                _rules.Add(rule);
            }
        }
    }

    /// <summary>True if the path is ignored by the rules added so far.</summary>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        var ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.Matches(relativePath, isDirectory))
            {
                ignored = !rule.IsNegated;
            }
        }

        return ignored;
    }
}
