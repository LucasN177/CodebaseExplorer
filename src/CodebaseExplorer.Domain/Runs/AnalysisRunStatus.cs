namespace CodebaseExplorer.Domain.Runs;

/// <summary>State of an analysis run. Terminal states are final.</summary>
public enum AnalysisRunStatus
{
    Pending,
    Running,
    Succeeded,
    PartiallySucceeded,
    Failed,
    Cancelled,
}
