namespace CodebaseExplorer.Application.Analysis;

/// <summary>Stable <see cref="Common.ApplicationError"/> and diagnostic codes of the analysis pipeline.</summary>
public static class AnalysisErrorCodes
{
    /// <summary>The linked model violates cross-element invariants; the run fails.</summary>
    public const string ModelInvalid = "analysis.model-invalid";

    /// <summary>Diagnostic: an analyzer threw; its results for that scope are missing.</summary>
    public const string AnalyzerFailed = "analysis.analyzer-failed";
}
