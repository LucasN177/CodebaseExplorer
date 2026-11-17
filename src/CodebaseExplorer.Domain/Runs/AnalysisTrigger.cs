namespace CodebaseExplorer.Domain.Runs;

/// <summary>Why an analysis run was started.</summary>
public enum AnalysisTrigger
{
    Initial,
    Manual,
    Incremental,
}
