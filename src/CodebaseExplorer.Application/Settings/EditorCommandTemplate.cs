using System.Globalization;
using System.Text;
using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.Settings;

/// <summary>
/// The user's external-editor command (UI-009, SECURITY.md §4 "Open in editor"): a program and its arguments, e.g.
/// <c>code --goto "{path}:{line}"</c>. The template is split into arguments once, here, without any shell: whitespace
/// separates arguments, double quotes group them. Placeholders <c>{path}</c>, <c>{line}</c> and <c>{column}</c> are then
/// replaced inside each argument, so a path with spaces, quotes, <c>;</c>, <c>&amp;</c> or <c>$(…)</c> always stays
/// exactly one argument and is never interpreted.
/// </summary>
public sealed class EditorCommandTemplate
{
    public const int MaxLength = 1000;

    private static readonly string[] Placeholders = ["{path}", "{line}", "{column}"];

    private EditorCommandTemplate(string text, IReadOnlyList<string> parts)
    {
        Text = text;
        Parts = parts;
    }

    /// <summary>The template as entered.</summary>
    public string Text { get; }

    /// <summary>Program followed by the argument templates.</summary>
    public IReadOnlyList<string> Parts { get; }

    public static Result<EditorCommandTemplate> Parse(string? template)
    {
        var text = template?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxLength || text.Any(c => char.IsControl(c)))
        {
            return Invalid($"Enter a command of at most {MaxLength} characters on one line, e.g. code --goto \"{{path}}:{{line}}\".");
        }

        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (inQuotes)
        {
            return Invalid("A quote is not closed.");
        }

        if (hasToken)
        {
            parts.Add(current.ToString());
        }

        if (parts.Count == 0 || parts[0].Length == 0 || parts[0].Contains('{', StringComparison.Ordinal))
        {
            return Invalid("The command must start with the editor program (a name or full path).");
        }

        if (!parts.Skip(1).Any(p => p.Contains("{path}", StringComparison.Ordinal)))
        {
            return Invalid("The arguments must contain {path} (the file to open).");
        }

        if (parts.FirstOrDefault(p => Unknown(p)) is { } unknown)
        {
            return Invalid($"'{unknown}' contains an unknown placeholder; use {{path}}, {{line}} and {{column}}.");
        }

        return new EditorCommandTemplate(text, parts);
    }

    /// <summary>The program and the arguments for one file; every argument is passed as is, without a shell.</summary>
    public EditorLaunch Build(string absolutePath, int line, int column = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        var lineText = Math.Max(1, line).ToString(CultureInfo.InvariantCulture);
        var columnText = Math.Max(1, column).ToString(CultureInfo.InvariantCulture);
        return new EditorLaunch(Parts[0], [.. Parts.Skip(1).Select(part =>
            // One pass, so text inserted for {path} is never searched for further placeholders.
            Replace(part, absolutePath, lineText, columnText))]);
    }

    private static string Replace(string part, string path, string line, string column)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < part.Length;)
        {
            var placeholder = Placeholders.FirstOrDefault(p => string.CompareOrdinal(part, i, p, 0, p.Length) == 0);
            if (placeholder is null)
            {
                builder.Append(part[i]);
                i++;
                continue;
            }

            builder.Append(placeholder switch
            {
                "{path}" => path,
                "{line}" => line,
                _ => column,
            });
            i += placeholder.Length;
        }

        return builder.ToString();
    }

    private static bool Unknown(string part)
    {
        var rest = part;
        foreach (var placeholder in Placeholders)
        {
            rest = rest.Replace(placeholder, string.Empty, StringComparison.Ordinal);
        }

        var open = rest.IndexOf('{', StringComparison.Ordinal);
        return open >= 0 && rest.IndexOf('}', open) > open;
    }

    private static ApplicationError Invalid(string message) => ApplicationError.Validation(EditorErrorCodes.TemplateInvalid, message);
}

/// <summary>A program to start with its arguments (each passed separately, never through a shell).</summary>
public sealed record EditorLaunch(string FileName, IReadOnlyList<string> Arguments);

/// <summary>Error codes of the external-editor feature (UI-009).</summary>
public static class EditorErrorCodes
{
    public const string TemplateInvalid = "editor.template-invalid";

    public const string NotConfigured = "editor.not-configured";

    public const string LaunchFailed = "editor.launch-failed";
}
