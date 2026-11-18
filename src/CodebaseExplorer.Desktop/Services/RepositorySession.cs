using CodebaseExplorer.Contracts.Repositories;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>
/// The repository the user is currently working with in this window (UI state only; the model itself is
/// stored by the Application layer). Set after a successful import or when a stored repository is opened
/// (REPO-005); cleared when it is removed. The shell navigates and the pages load their data when it
/// changes. Used from the UI thread.
/// </summary>
public sealed class RepositorySession
{
    /// <summary>The open repository, or <see langword="null"/>.</summary>
    public OpenRepositoryDto? Current { get; private set; }

    /// <summary>Raised after <see cref="Current"/> changed (also when the same repository was re-imported or reopened).</summary>
    public event EventHandler? CurrentChanged;

    public void SetCurrent(OpenRepositoryDto repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        Current = repository;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes the open repository (e.g. after it was removed).</summary>
    public void Clear()
    {
        if (Current is null)
        {
            return;
        }

        Current = null;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
