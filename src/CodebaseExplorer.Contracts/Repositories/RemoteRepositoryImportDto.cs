namespace CodebaseExplorer.Contracts.Repositories;

/// <summary>Outcome of cloning (or updating) and analyzing a remote repository (GIT-003), for display.</summary>
public sealed record RemoteRepositoryImportDto
{
    public required RepositoryImportSummaryDto Summary { get; init; }

    /// <summary>The remote URL as stored: HTTPS, without credentials.</summary>
    public required string RemoteUrl { get; init; }

    /// <summary>Absolute path of the app-managed clone.</summary>
    public required string ClonePath { get; init; }

    /// <summary>Checked-out branch; <see langword="null"/> if HEAD is detached.</summary>
    public string? Branch { get; init; }

    /// <summary><see langword="true"/> if an existing clone was updated instead of cloned.</summary>
    public bool Updated { get; init; }

    /// <summary>The entered URL contained credentials, which were removed (warn the user; use the token field instead).</summary>
    public bool CredentialsRemovedFromUrl { get; init; }

    /// <summary>The repository declares submodules; they were not cloned and are not analyzed.</summary>
    public bool HasSubmodules { get; init; }
}
