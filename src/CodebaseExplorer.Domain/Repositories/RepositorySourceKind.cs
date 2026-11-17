namespace CodebaseExplorer.Domain.Repositories;

/// <summary>Where a registered repository comes from.</summary>
public enum RepositorySourceKind
{
    Local,
    GitHub,
    GitLab,
    OtherGitRemote,
}
