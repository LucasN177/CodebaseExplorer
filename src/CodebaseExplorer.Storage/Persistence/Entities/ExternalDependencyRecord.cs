using CodebaseExplorer.Domain.Dependencies;

namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>ExternalDependencies</c> (§4.11).</summary>
internal sealed class ExternalDependencyRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public PackageEcosystem Ecosystem { get; set; }

    /// <summary>Normalized name (identity-relevant form).</summary>
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string NaturalKey { get; set; } = string.Empty;

    public Guid LastSeenRunId { get; set; }
}
