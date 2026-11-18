using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Languages.TypeScript;

/// <summary>Outcome of resolving a module path against the scanned files.</summary>
internal enum FileResolution
{
    /// <summary>Resolved to a TypeScript/JavaScript source file.</summary>
    SourceFile,

    /// <summary>Resolved to an existing file that is not a source module (JSON, CSS, images …).</summary>
    OtherFile,

    /// <summary>Nothing exists at the path.</summary>
    Missing,
}

/// <summary>
/// File lookup with the extension and <c>index</c> rules of Node and TypeScript (ANALYSIS_ENGINE.md Stage 3), on the
/// scanned file list only — the file system is never probed.
/// </summary>
internal sealed class ModuleFiles(IEnumerable<RelativePath> scannedFiles)
{
    /// <summary>Extensions tried for an extension-less specifier, in TypeScript's order.</summary>
    private static readonly string[] Extensions = [".ts", ".tsx", ".d.ts", ".js", ".jsx", ".mts", ".cts", ".mjs", ".cjs"];

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ts", ".tsx", ".js", ".jsx", ".mts", ".cts", ".mjs", ".cjs",
    };

    /// <summary>TypeScript maps an emitted <c>.js</c> specifier back to its source (<c>./a.js</c> → <c>./a.ts</c>).</summary>
    private static readonly (string Emitted, string[] Sources)[] EmittedToSource =
    [
        (".js", [".ts", ".tsx"]), (".jsx", [".tsx"]), (".mjs", [".mts"]), (".cjs", [".cts"]),
    ];

    private readonly HashSet<string> _files = scannedFiles.Select(f => f.Value).ToHashSet(StringComparer.Ordinal);

    public static bool IsSourceFile(RelativePath path) => SourceExtensions.Contains(Path.GetExtension(path.FileName));

    public (FileResolution Kind, RelativePath? File) Resolve(RelativePath path)
    {
        if (!path.IsRoot && _files.Contains(path.Value))
        {
            return IsSourceFile(path) ? (FileResolution.SourceFile, path) : (FileResolution.OtherFile, path);
        }

        var value = path.IsRoot ? string.Empty : path.Value;
        if (!path.IsRoot)
        {
            foreach (var extension in Extensions)
            {
                if (_files.Contains(value + extension))
                {
                    return (FileResolution.SourceFile, RelativePath.Create(value + extension));
                }
            }

            foreach (var (emitted, sources) in EmittedToSource)
            {
                if (value.EndsWith(emitted, StringComparison.OrdinalIgnoreCase))
                {
                    var stem = value[..^emitted.Length];
                    foreach (var source in sources)
                    {
                        if (_files.Contains(stem + source))
                        {
                            return (FileResolution.SourceFile, RelativePath.Create(stem + source));
                        }
                    }
                }
            }
        }

        var directory = path.IsRoot ? string.Empty : value + "/";
        foreach (var extension in Extensions)
        {
            if (_files.Contains(directory + "index" + extension))
            {
                return (FileResolution.SourceFile, RelativePath.Create(directory + "index" + extension));
            }
        }

        return (FileResolution.Missing, null);
    }
}
