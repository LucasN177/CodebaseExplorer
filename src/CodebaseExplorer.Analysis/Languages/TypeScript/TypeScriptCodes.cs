using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Languages.TypeScript;

/// <summary>Analyzer id, rules and diagnostic codes of the Tree-sitter TypeScript/JavaScript analyzer.</summary>
internal static class TypeScriptCodes
{
    /// <summary>One analyzer for TypeScript, TSX and JavaScript (shared resolution rules).</summary>
    public const string AnalyzerName = "treesitter.typescript";

    public const string Version = "1.0.0";

    public static readonly AnalyzerId Analyzer = AnalyzerId.Create(AnalyzerName);

    // Rule parts: ts.{syntax}.{resolution}
    public const string SyntaxImport = "import";
    public const string SyntaxExport = "export";
    public const string SyntaxRequire = "require";
    public const string SyntaxDynamicImport = "dynamic-import";

    public const string ResolutionRelative = "relative";
    public const string ResolutionPathAlias = "path-alias";
    public const string ResolutionWorkspace = "workspace";
    public const string ResolutionPackage = "package";
    public const string ResolutionPackageUndeclared = "package-undeclared";

    public static string Rule(string syntax, string resolution) => $"ts.{syntax}.{resolution}";

    // Diagnostics
    public const string FileNotRead = "ts.file.not-read";
    public const string SyntaxErrors = "ts.file.syntax-errors";
    public const string ImportUnresolved = "ts.import.unresolved";
    public const string ImportComputed = "ts.import.computed";
    public const string ImportOutsideProjects = "ts.import.outside-projects";
    public const string TsConfigNotRead = "ts.tsconfig.not-read";
}
