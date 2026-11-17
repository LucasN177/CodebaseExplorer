using System.Text;
using System.Text.RegularExpressions;

namespace CodebaseExplorer.Analysis.Scanning;

/// <summary>
/// One pattern line of a <c>.gitignore</c> file, compiled to a regular expression. Implements the
/// pattern format of gitignore(5): <c>!</c> negation, trailing <c>/</c> for directories only,
/// anchoring when a <c>/</c> appears at the start or in the middle, <c>*</c>, <c>?</c>,
/// <c>[...]</c> classes, <c>**</c> in leading, trailing and middle positions, and <c>\</c> escapes.
/// </summary>
internal sealed class GitIgnoreRule
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private readonly Regex _regex;

    private GitIgnoreRule(string baseDirectory, string pattern, bool negated, bool directoryOnly, Regex regex)
    {
        BaseDirectory = baseDirectory;
        Pattern = pattern;
        IsNegated = negated;
        IsDirectoryOnly = directoryOnly;
        _regex = regex;
    }

    /// <summary>Repository-relative directory of the <c>.gitignore</c> file (empty for the root).</summary>
    public string BaseDirectory { get; }

    /// <summary>The pattern as written (after removing negation and trailing whitespace).</summary>
    public string Pattern { get; }

    public bool IsNegated { get; }

    public bool IsDirectoryOnly { get; }

    /// <summary>Parses one line; returns <see langword="null"/> for blank lines and comments.</summary>
    public static GitIgnoreRule? Parse(string line, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(baseDirectory);

        var text = TrimTrailingUnescapedSpaces(line.TrimEnd('\r'));
        if (text.Length == 0 || text.StartsWith('#'))
        {
            return null;
        }

        var negated = false;
        if (text.StartsWith('!'))
        {
            negated = true;
            text = text[1..];
        }
        else if (text.StartsWith(@"\!", StringComparison.Ordinal) || text.StartsWith(@"\#", StringComparison.Ordinal))
        {
            text = text[1..];
        }

        var directoryOnly = text.EndsWith('/') && !text.EndsWith(@"\/", StringComparison.Ordinal);
        if (directoryOnly)
        {
            text = text.TrimEnd('/');
        }

        if (text.Length == 0)
        {
            return null;
        }

        // A slash at the start or in the middle anchors the pattern to the .gitignore's directory.
        var anchored = text.Contains('/');
        var body = text.StartsWith('/') ? text[1..] : text;
        if (body.Length == 0)
        {
            return null;
        }

        var regex = new StringBuilder("^");
        if (!anchored)
        {
            regex.Append("(?:.*/)?");
        }

        regex.Append(Translate(body));
        regex.Append('$');

        var compiled = new Regex(regex.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline, MatchTimeout);
        return new GitIgnoreRule(baseDirectory, text, negated, directoryOnly, compiled);
    }

    /// <summary>
    /// True if the rule matches <paramref name="relativePath"/> (repository-relative, <c>/</c>-separated).
    /// Paths outside <see cref="BaseDirectory"/> never match.
    /// </summary>
    public bool Matches(string relativePath, bool isDirectory)
    {
        if (IsDirectoryOnly && !isDirectory)
        {
            return false;
        }

        string local;
        if (BaseDirectory.Length == 0)
        {
            local = relativePath;
        }
        else if (relativePath.StartsWith(BaseDirectory + "/", StringComparison.Ordinal))
        {
            local = relativePath[(BaseDirectory.Length + 1)..];
        }
        else
        {
            return false;
        }

        try
        {
            return _regex.IsMatch(local);
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological patterns from untrusted repositories are treated as non-matching.
            return false;
        }
    }

    private static string Translate(string pattern)
    {
        var builder = new StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                var atStart = i == 0;
                var precededBySlash = i > 0 && pattern[i - 1] == '/';
                var followedBySlash = i + 2 < pattern.Length && pattern[i + 2] == '/';
                var atEnd = i + 2 == pattern.Length;

                if ((atStart || precededBySlash) && followedBySlash)
                {
                    builder.Append("(?:.*/)?"); // "**/" : zero or more directories
                    i += 3;
                    continue;
                }

                if (precededBySlash && atEnd)
                {
                    builder.Append(".*"); // "/**" : everything inside
                    i += 2;
                    continue;
                }

                // Other consecutive asterisks are regular asterisks.
                builder.Append("[^/]*");
                while (i < pattern.Length && pattern[i] == '*')
                {
                    i++;
                }

                continue;
            }

            switch (c)
            {
                case '*':
                    builder.Append("[^/]*");
                    i++;
                    break;
                case '?':
                    builder.Append("[^/]");
                    i++;
                    break;
                case '[':
                    i = AppendCharacterClass(pattern, i, builder);
                    break;
                case '\\' when i + 1 < pattern.Length:
                    builder.Append(Regex.Escape(pattern[i + 1].ToString()));
                    i += 2;
                    break;
                default:
                    builder.Append(Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }

        return builder.ToString();
    }

    private static int AppendCharacterClass(string pattern, int start, StringBuilder builder)
    {
        var end = pattern.IndexOf(']', start + 2);
        if (end < 0)
        {
            builder.Append(@"\[");
            return start + 1;
        }

        var content = pattern[(start + 1)..end];
        var negate = content.StartsWith('!') || content.StartsWith('^');
        if (negate)
        {
            content = content[1..];
        }

        builder.Append('[');
        if (negate)
        {
            builder.Append('^');
        }

        foreach (var ch in content)
        {
            builder.Append(ch is '\\' or ']' or '[' or '^' ? "\\" + ch : ch.ToString());
        }

        builder.Append(']');
        return end + 1;
    }

    private static string TrimTrailingUnescapedSpaces(string text)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] == ' ' && !(end > 1 && text[end - 2] == '\\'))
        {
            end--;
        }

        var trimmed = text[..end];
        return trimmed.EndsWith(@"\ ", StringComparison.Ordinal) ? trimmed[..^2] + " " : trimmed;
    }
}
