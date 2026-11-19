using System.Text;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>Kind of a <see cref="MarkdownBlock"/>.</summary>
public enum MarkdownBlockKind
{
    Paragraph,
    Heading,
    Bullet,
    Code,
}

/// <summary>
/// One block of an AI answer as plain text (AI-006, AI_ARCHITECTURE.md §5): the views show every block in a text
/// element, so HTML, images and links in model output are never rendered — they appear as the literal text.
/// </summary>
public sealed record MarkdownBlock(MarkdownBlockKind Kind, string Text, string? Language = null)
{
    public bool IsParagraph => Kind == MarkdownBlockKind.Paragraph;

    public bool IsHeading => Kind == MarkdownBlockKind.Heading;

    public bool IsBullet => Kind == MarkdownBlockKind.Bullet;

    public bool IsCode => Kind == MarkdownBlockKind.Code;
}

/// <summary>
/// A deliberately small Markdown reader for AI answers: fenced code blocks, ATX headings, list items and paragraphs.
/// Emphasis markers (<c>**</c>, <c>__</c>) are removed; everything else, including HTML, stays literal text.
/// </summary>
public static class MarkdownBlocks
{
    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var blocks = new List<MarkdownBlock>();
        var paragraph = new StringBuilder();
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                Flush();
                var fence = trimmed[..3];
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal); i++)
                {
                    code.Add(lines[i]);
                }

                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code, string.Join('\n', code), language.Length == 0 ? null : language));
                continue;
            }

            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }

            if (Heading(trimmed) is { } heading)
            {
                Flush();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, Inline(heading)));
                continue;
            }

            if (ListItem(trimmed) is { } item)
            {
                Flush();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Bullet, Inline(item)));
                continue;
            }

            paragraph.Append(paragraph.Length == 0 ? string.Empty : "\n").Append(line.Trim());
        }

        Flush();
        return blocks;

        void Flush()
        {
            if (paragraph.Length > 0)
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, Inline(paragraph.ToString())));
                paragraph.Clear();
            }
        }
    }

    private static string? Heading(string line)
    {
        var level = 0;
        while (level < line.Length && level < 6 && line[level] == '#')
        {
            level++;
        }

        return level > 0 && level < line.Length && line[level] == ' ' ? line[(level + 1)..].Trim().TrimEnd('#').Trim() : null;
    }

    /// <summary>"- x", "* x", "+ x" → "• x"; "1. x" stays numbered.</summary>
    private static string? ListItem(string line)
    {
        if (line.Length > 2 && line[0] is '-' or '*' or '+' && line[1] == ' ')
        {
            return "• " + line[2..].Trim();
        }

        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        return digits is > 0 and < 4 && digits + 1 < line.Length && line[digits] is '.' or ')' && line[digits + 1] == ' '
            ? line[..(digits + 1)] + " " + line[(digits + 2)..].Trim()
            : null;
    }

    private static string Inline(string text) => text.Replace("**", string.Empty, StringComparison.Ordinal).Replace("__", string.Empty, StringComparison.Ordinal);
}
