using System.Text;

namespace CodebaseExplorer.Application.Export;

/// <summary>
/// Escaping of repository-provided text (names, paths, versions) for Markdown (EXPORT-001). Names come from untrusted
/// repositories: they must not break tables, inject links, images or HTML, or start headings.
/// </summary>
public static class MarkdownText
{
    // Only characters that are markup anywhere in a line; escaped text never starts a line, so "#", "-", "+" and "."
    // need no escape, and "(" / "!" are harmless once "[" and "]" are escaped (no link or image can form).
    private const string Punctuation = "\\`*_[]|~";

    /// <summary>Text for a paragraph or table cell: Markdown punctuation backslash-escaped, HTML characters as entities, line breaks as spaces.</summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '\r' or '\n' or '\t':
                    builder.Append(' ');
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append(' ');
                    }
                    else
                    {
                        if (Punctuation.Contains(c, StringComparison.Ordinal))
                        {
                            builder.Append('\\');
                        }

                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
