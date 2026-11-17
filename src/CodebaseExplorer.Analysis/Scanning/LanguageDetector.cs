using System.Text;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Scanning;

/// <summary>
/// Maps files to a <see cref="LanguageId"/> by well-known file name, then extension, then — only for
/// files without extension — the shebang line. Anything not in the tables is <c>unknown</c>; content
/// is never guessed.
/// </summary>
internal static class LanguageDetector
{
    private static readonly Dictionary<string, string> FileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dockerfile"] = "dockerfile",
        ["Makefile"] = "makefile",
        ["CMakeLists.txt"] = "cmake",
        [".gitignore"] = "gitignore",
        [".editorconfig"] = "editorconfig",
        ["global.json"] = "json",
    };

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // .NET
        [".cs"] = "csharp",
        [".csx"] = "csharp",
        [".fs"] = "fsharp",
        [".fsi"] = "fsharp",
        [".fsx"] = "fsharp",
        [".vb"] = "vb",
        [".razor"] = "razor",
        [".cshtml"] = "razor",
        [".xaml"] = "xaml",
        [".axaml"] = "xaml",
        [".sln"] = "sln",

        // MSBuild and other XML
        [".slnx"] = "xml",
        [".csproj"] = "xml",
        [".fsproj"] = "xml",
        [".vbproj"] = "xml",
        [".props"] = "xml",
        [".targets"] = "xml",
        [".resx"] = "xml",
        [".config"] = "xml",
        [".xml"] = "xml",
        [".nuspec"] = "xml",

        // TypeScript / JavaScript
        [".ts"] = "typescript",
        [".mts"] = "typescript",
        [".cts"] = "typescript",
        [".tsx"] = "tsx",
        [".js"] = "javascript",
        [".mjs"] = "javascript",
        [".cjs"] = "javascript",
        [".jsx"] = "javascript",

        // Python
        [".py"] = "python",
        [".pyi"] = "python",
        [".pyw"] = "python",

        // Data and configuration
        [".json"] = "json",
        [".jsonc"] = "json",
        [".yml"] = "yaml",
        [".yaml"] = "yaml",
        [".toml"] = "toml",
        [".ini"] = "ini",
        [".cfg"] = "ini",

        // Documentation
        [".md"] = "markdown",
        [".markdown"] = "markdown",
        [".rst"] = "restructuredtext",
        [".txt"] = "text",

        // Web
        [".html"] = "html",
        [".htm"] = "html",
        [".css"] = "css",
        [".scss"] = "scss",

        // Scripts and other languages commonly found next to the supported ones
        [".sh"] = "shell",
        [".bash"] = "shell",
        [".ps1"] = "powershell",
        [".psm1"] = "powershell",
        [".sql"] = "sql",
    };

    private static readonly Dictionary<string, string> ShebangInterpreters = new(StringComparer.Ordinal)
    {
        ["python"] = "python",
        ["python3"] = "python",
        ["python2"] = "python",
        ["node"] = "javascript",
        ["deno"] = "typescript",
        ["sh"] = "shell",
        ["bash"] = "shell",
        ["zsh"] = "shell",
        ["pwsh"] = "powershell",
    };

    /// <summary>Languages whose files are classified as source code.</summary>
    public static readonly IReadOnlySet<string> SourceLanguages = new HashSet<string>(StringComparer.Ordinal)
    {
        "csharp", "fsharp", "vb", "razor", "xaml", "typescript", "tsx", "javascript", "python",
        "html", "css", "scss", "shell", "powershell", "sql",
    };

    /// <summary>Languages whose files are classified as documentation.</summary>
    public static readonly IReadOnlySet<string> DocumentationLanguages = new HashSet<string>(StringComparer.Ordinal)
    {
        "markdown", "restructuredtext", "text",
    };

    /// <summary>Languages whose files are classified as configuration.</summary>
    public static readonly IReadOnlySet<string> ConfigurationLanguages = new HashSet<string>(StringComparer.Ordinal)
    {
        "json", "xml", "yaml", "toml", "ini", "gitignore", "editorconfig", "dockerfile", "makefile", "cmake",
    };

    /// <param name="fileName">File name without directory.</param>
    /// <param name="head">The first bytes of the file (used for shebang lines only).</param>
    public static LanguageId Detect(string fileName, ReadOnlySpan<byte> head)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        if (FileNames.TryGetValue(fileName, out var byName))
        {
            return LanguageId.Create(byName);
        }

        var extension = Path.GetExtension(fileName);
        if (extension.Length > 0 && !(extension == fileName))
        {
            return Extensions.TryGetValue(extension, out var byExtension) ? LanguageId.Create(byExtension) : LanguageId.Unknown;
        }

        return FromShebang(head);
    }

    private static LanguageId FromShebang(ReadOnlySpan<byte> head)
    {
        if (head.Length < 3 || head[0] != '#' || head[1] != '!')
        {
            return LanguageId.Unknown;
        }

        var lineEnd = head.IndexOf((byte)'\n');
        var line = Encoding.ASCII.GetString(lineEnd < 0 ? head[2..] : head[2..lineEnd]).Trim();
        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return LanguageId.Unknown;
        }

        // "#!/usr/bin/env python3" or "#!/usr/bin/python3"; "env -S node" is supported.
        var interpreter = Path.GetFileName(parts[0]);
        if (interpreter == "env")
        {
            interpreter = parts.Skip(1).FirstOrDefault(p => !p.StartsWith('-')) ?? string.Empty;
        }

        return ShebangInterpreters.TryGetValue(interpreter, out var language) ? LanguageId.Create(language) : LanguageId.Unknown;
    }
}
