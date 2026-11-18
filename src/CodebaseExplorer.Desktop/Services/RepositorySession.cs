using CodebaseExplorer.Contracts.Repositories;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>
/// The repository the user is currently working with in this window (UI state only; the analysis
/// result itself lives in the Application layer's model cache). Set after a successful import;
/// the shell navigates to the Architecture Explorer and the explorer loads the graph when it changes.
/// Used from the UI thread.
/// </summary>
public sealed class RepositorySession
{
    /// <summary>The last successfully imported repository, or <see langword="null"/>.</summary>
    public RepositoryImportSummaryDto? Current { get; private set; }

    /// <summary>Raised after <see cref="Current"/> changed (also when the same folder was re-imported).</summary>
    public event EventHandler? CurrentChanged;

    public void SetCurrent(RepositoryImportSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Current = summary;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
