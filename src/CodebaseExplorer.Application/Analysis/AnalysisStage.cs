namespace CodebaseExplorer.Application.Analysis;

/// <summary>Stages of an import/analysis as shown to the user (ANALYSIS_ENGINE.md §2, USER_WORKFLOWS.md WF-01).</summary>
public enum AnalysisStage
{
    Preparing,
    Scanning,
    DetectingProjects,
    AnalyzingCode,
    Linking,
    Validating,
    Saving,
}
