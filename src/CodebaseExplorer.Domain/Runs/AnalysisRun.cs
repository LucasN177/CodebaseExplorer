using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Runs;

/// <summary>
/// One execution of the analysis pipeline (DOMAIN_MODEL.md §4.2). State machine:
/// <c>Pending → Running → (Succeeded | PartiallySucceeded | Failed | Cancelled)</c>; a pending run
/// may also fail or be cancelled. Terminal states are final.
/// </summary>
public sealed class AnalysisRun
{
    private readonly SortedDictionary<string, string> _analyzerVersions = new(StringComparer.Ordinal);
    private readonly List<AnalysisDiagnostic> _diagnostics = [];

    private AnalysisRun(
        AnalysisRunId id,
        RepositoryId repositoryId,
        AnalysisTrigger trigger,
        DateTimeOffset startedAt,
        string? commitSha,
        string? branch)
    {
        Id = id;
        RepositoryId = repositoryId;
        Trigger = trigger;
        StartedAt = startedAt;
        CommitSha = commitSha;
        Branch = branch;
        Status = AnalysisRunStatus.Pending;
        Statistics = AnalysisStatistics.Empty;
    }

    public AnalysisRunId Id { get; }

    public RepositoryId RepositoryId { get; }

    public AnalysisRunStatus Status { get; private set; }

    public AnalysisTrigger Trigger { get; }

    /// <summary>When the run was requested.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>When the run reached a terminal state.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>HEAD commit at analysis time, if the repository is a Git repository.</summary>
    public string? CommitSha { get; }

    public string? Branch { get; }

    public bool IsTerminal => Status is not (AnalysisRunStatus.Pending or AnalysisRunStatus.Running);

    /// <summary>True if the run produced a usable model (succeeded, possibly with errors).</summary>
    public bool ProducedModel => Status is AnalysisRunStatus.Succeeded or AnalysisRunStatus.PartiallySucceeded;

    /// <summary>Analyzer id → version, for reproducibility.</summary>
    public IReadOnlyDictionary<string, string> AnalyzerVersions => _analyzerVersions.AsReadOnly();

    public AnalysisStatistics Statistics { get; private set; }

    public IReadOnlyList<AnalysisDiagnostic> Diagnostics => _diagnostics.AsReadOnly();

    /// <summary>Creates a pending run with a fresh time-ordered id.</summary>
    public static AnalysisRun Create(
        RepositoryId repositoryId,
        AnalysisTrigger trigger,
        DateTimeOffset startedAt,
        string? commitSha = null,
        string? branch = null)
    {
        if (repositoryId.Value == Guid.Empty)
        {
            throw new ArgumentException("A run requires a repository id.", nameof(repositoryId));
        }

        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null);
        }

        return new AnalysisRun(
            AnalysisRunId.New(startedAt), repositoryId, trigger, startedAt, ValidateCommitSha(commitSha), Blank(branch));
    }

    /// <summary>Restores a run in any valid state (e.g. from storage).</summary>
    /// <exception cref="ArgumentException">The combination of values is not a valid run state.</exception>
    public static AnalysisRun Restore(
        AnalysisRunId id,
        RepositoryId repositoryId,
        AnalysisTrigger trigger,
        AnalysisRunStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        string? commitSha,
        string? branch,
        IReadOnlyDictionary<string, string> analyzerVersions,
        AnalysisStatistics statistics,
        IEnumerable<AnalysisDiagnostic> diagnostics)
    {
        if (id.Value == Guid.Empty || repositoryId.Value == Guid.Empty)
        {
            throw new ArgumentException("A run requires an id and a repository id.");
        }

        if (!Enum.IsDefined(trigger) || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        ArgumentNullException.ThrowIfNull(analyzerVersions);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var run = new AnalysisRun(id, repositoryId, trigger, startedAt, ValidateCommitSha(commitSha), Blank(branch))
        {
            Status = status,
            Statistics = statistics,
        };

        if (run.IsTerminal != completedAt.HasValue)
        {
            throw new ArgumentException("Exactly the terminal states have a completion time.", nameof(completedAt));
        }

        if (completedAt < startedAt)
        {
            throw new ArgumentException("A run cannot complete before it started.", nameof(completedAt));
        }

        run.CompletedAt = completedAt;

        foreach (var (analyzer, version) in analyzerVersions)
        {
            run.AddAnalyzerVersion(AnalyzerId.Create(analyzer), version);
        }

        run._diagnostics.AddRange(diagnostics.Select(d => d ?? throw new ArgumentException("Diagnostics must not contain null.", nameof(diagnostics))));

        if (status == AnalysisRunStatus.Failed && !run.HasErrors)
        {
            throw new ArgumentException("A failed run must report at least one error diagnostic.", nameof(diagnostics));
        }

        if (status == AnalysisRunStatus.Succeeded && run.HasErrors)
        {
            throw new ArgumentException("A run with error diagnostics is at most partially succeeded.", nameof(status));
        }

        if (status == AnalysisRunStatus.PartiallySucceeded && !run.HasErrors)
        {
            throw new ArgumentException("A partially succeeded run must report at least one error diagnostic.", nameof(diagnostics));
        }

        return run;
    }

    /// <summary><c>Pending → Running</c>.</summary>
    /// <exception cref="InvalidOperationException">The run is not pending.</exception>
    public void Start()
    {
        EnsureStatus(AnalysisRunStatus.Pending);
        Status = AnalysisRunStatus.Running;
    }

    /// <summary>Records the version of an analyzer taking part in the run (pending or running only).</summary>
    public void RegisterAnalyzer(AnalyzerId analyzer, string version)
    {
        EnsureNotTerminal();
        AddAnalyzerVersion(analyzer, version);
    }

    /// <summary>Adds diagnostics while the run is pending or running.</summary>
    public void Report(IEnumerable<AnalysisDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        EnsureNotTerminal();
        _diagnostics.AddRange(diagnostics.Select(d => d ?? throw new ArgumentException("Diagnostics must not contain null.", nameof(diagnostics))));
    }

    /// <summary>
    /// <c>Running → Succeeded</c>, or <c>PartiallySucceeded</c> if any error diagnostic was reported.
    /// </summary>
    public void Complete(DateTimeOffset completedAt, AnalysisStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        EnsureStatus(AnalysisRunStatus.Running);
        Finish(completedAt, HasErrors ? AnalysisRunStatus.PartiallySucceeded : AnalysisRunStatus.Succeeded);
        Statistics = statistics;
    }

    /// <summary><c>Pending | Running → Failed</c>; requires at least one error diagnostic.</summary>
    public void Fail(DateTimeOffset completedAt, IEnumerable<AnalysisDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        EnsureNotTerminal();

        var additional = diagnostics.ToArray();
        if (!HasErrors && additional.All(d => d.Severity != DiagnosticSeverity.Error))
        {
            throw new ArgumentException("A failed run must report at least one error diagnostic.", nameof(diagnostics));
        }

        Report(additional);
        Finish(completedAt, AnalysisRunStatus.Failed);
    }

    /// <summary><c>Pending | Running → Cancelled</c>.</summary>
    public void Cancel(DateTimeOffset completedAt)
    {
        EnsureNotTerminal();
        Finish(completedAt, AnalysisRunStatus.Cancelled);
    }

    private bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    private void Finish(DateTimeOffset completedAt, AnalysisRunStatus status)
    {
        if (completedAt < StartedAt)
        {
            throw new ArgumentException("A run cannot complete before it started.", nameof(completedAt));
        }

        Status = status;
        CompletedAt = completedAt;
    }

    private void AddAnalyzerVersion(AnalyzerId analyzer, string version)
    {
        if (analyzer.Value is null)
        {
            throw new ArgumentException("The analyzer id must be initialized.", nameof(analyzer));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _analyzerVersions[analyzer.Value] = version.Trim();
    }

    private void EnsureStatus(AnalysisRunStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"The run is {Status}; expected {expected}.");
        }
    }

    private void EnsureNotTerminal()
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"The run is already {Status}.");
        }
    }

    private static string? ValidateCommitSha(string? commitSha)
    {
        if (string.IsNullOrWhiteSpace(commitSha))
        {
            return null;
        }

        var sha = commitSha.Trim().ToLowerInvariant();
        if (sha.Length is not (40 or 64) || !sha.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException($"'{commitSha}' is not a full SHA-1 or SHA-256 commit id.", nameof(commitSha));
        }

        return sha;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
