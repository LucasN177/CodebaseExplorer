using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Dependencies;

/// <summary>
/// A package from outside the repository (DOMAIN_MODEL.md §4.9). How a project uses it (version
/// specification, dev-only …) is stored on the <c>PackageReference</c> relationship.
/// </summary>
public sealed class ExternalDependency
{
    private ExternalDependency(RepositoryId repositoryId, PackageEcosystem ecosystem, string displayName)
    {
        var ecosystemToken = ecosystem.ToKeyToken();
        NaturalKey = NaturalKey.ForExternalDependency(repositoryId, ecosystemToken, displayName);
        Id = ElementId.FromNaturalKey(NaturalKey);
        RepositoryId = repositoryId;
        Ecosystem = ecosystem;
        Name = NaturalKey.NormalizePackageName(ecosystemToken, displayName);
        DisplayName = displayName;
    }

    public ElementId Id { get; }

    public NaturalKey NaturalKey { get; }

    public RepositoryId RepositoryId { get; }

    public PackageEcosystem Ecosystem { get; }

    /// <summary>Name normalized per ecosystem (the identity-relevant form).</summary>
    public string Name { get; }

    /// <summary>Name as written in the manifest.</summary>
    public string DisplayName { get; }

    /// <exception cref="ArgumentException">An argument violates a dependency invariant.</exception>
    public static ExternalDependency Create(RepositoryId repositoryId, PackageEcosystem ecosystem, string displayName)
    {
        if (repositoryId.Value == Guid.Empty)
        {
            throw new ArgumentException("An external dependency requires a repository id.", nameof(repositoryId));
        }

        if (!Enum.IsDefined(ecosystem))
        {
            throw new ArgumentOutOfRangeException(nameof(ecosystem), ecosystem, null);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new ExternalDependency(repositoryId, ecosystem, displayName.Trim());
    }
}
