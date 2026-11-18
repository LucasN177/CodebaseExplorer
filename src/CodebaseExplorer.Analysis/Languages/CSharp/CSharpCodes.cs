using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Languages.CSharp;

/// <summary>Analyzer id, version and diagnostic codes of the Roslyn C# analyzer.</summary>
internal static class CSharpCodes
{
    public const string AnalyzerName = "csharp.roslyn";

    public const string Version = "1.0.0";

    public static readonly AnalyzerId Analyzer = AnalyzerId.Create(AnalyzerName);

    // Diagnostics
    public const string FileNotRead = "csharp.file.not-read";
    public const string ProjectFailed = "csharp.project.failed";
    public const string ReferenceCycle = "csharp.project.reference-cycle";
}
