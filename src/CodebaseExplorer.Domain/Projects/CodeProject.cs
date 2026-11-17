using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;

namespace CodebaseExplorer.Domain.Projects;

/// <summary>
/// A unit of build or packaging: .NET project, npm package, Python project (DOMAIN_MODEL.md §4.4).
/// </summary>
public sealed class CodeProject
{
    private CodeProject(
        RepositoryId repositoryId,
        string name,
        ProjectKind kind,
        Ecosystem ecosystem,
        LanguageId primaryLanguage,
        RelativePath? projectFilePath,
        RelativePath rootDirectory,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<SolutionMembership> solutionMemberships)
    {
        NaturalKey = NaturalKey.ForProject(repositoryId, ecosystem.ToKeyToken(), (projectFilePath ?? rootDirectory).Value);
        Id = ElementId.FromNaturalKey(NaturalKey);
        RepositoryId = repositoryId;
        Name = name;
        Kind = kind;
        Ecosystem = ecosystem;
        PrimaryLanguage = primaryLanguage;
        ProjectFilePath = projectFilePath;
        ProjectFileId = projectFilePath is { } filePath ? RepositoryFile.IdFor(repositoryId, filePath) : null;
        RootDirectory = rootDirectory;
        Properties = properties;
        SolutionMemberships = solutionMemberships;
    }

    public ElementId Id { get; }

    /// <summary>Natural key; prefix of the keys of this project's modules and entities.</summary>
    public NaturalKey NaturalKey { get; }

    public RepositoryId RepositoryId { get; }

    public string Name { get; }

    public ProjectKind Kind { get; }

    public Ecosystem Ecosystem { get; }

    public LanguageId PrimaryLanguage { get; }

    /// <summary>Manifest file (<c>.csproj</c>, <c>package.json</c>, <c>pyproject.toml</c>), if the project has one.</summary>
    public RelativePath? ProjectFilePath { get; }

    /// <summary>Id of the manifest file, derived from <see cref="ProjectFilePath"/>.</summary>
    public ElementId? ProjectFileId { get; }

    public RelativePath RootDirectory { get; }

    /// <summary>Ecosystem-specific facts (target frameworks, output type, package version …).</summary>
    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<SolutionMembership> SolutionMemberships { get; }

    /// <summary>
    /// Creates a project. Its identity is the manifest path if present, otherwise the root directory.
    /// </summary>
    /// <exception cref="ArgumentException">An argument violates a project invariant.</exception>
    public static CodeProject Create(
        RepositoryId repositoryId,
        string name,
        ProjectKind kind,
        Ecosystem ecosystem,
        LanguageId primaryLanguage,
        RelativePath? projectFilePath,
        RelativePath rootDirectory,
        IReadOnlyDictionary<string, string>? properties = null,
        IEnumerable<SolutionMembership>? solutionMemberships = null)
    {
        if (repositoryId.Value == Guid.Empty)
        {
            throw new ArgumentException("A project requires a repository id.", nameof(repositoryId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        if (!Enum.IsDefined(ecosystem))
        {
            throw new ArgumentOutOfRangeException(nameof(ecosystem), ecosystem, null);
        }

        if (projectFilePath is { IsRoot: true })
        {
            throw new ArgumentException("A project file path must point to a file.", nameof(projectFilePath));
        }

        if (projectFilePath is null && rootDirectory.IsRoot)
        {
            throw new ArgumentException(
                "A project at the repository root needs a project file to be identifiable.", nameof(projectFilePath));
        }

        if (projectFilePath is { } file && !file.IsWithin(rootDirectory))
        {
            throw new ArgumentException(
                $"The project file '{file}' must lie within the root directory '{rootDirectory}'.", nameof(projectFilePath));
        }

        var copiedProperties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in properties ?? new Dictionary<string, string>())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(properties));
            copiedProperties[key] = value ?? throw new ArgumentException($"Property '{key}' has no value.", nameof(properties));
        }

        var memberships = (solutionMemberships ?? []).Distinct().ToArray();
        if (memberships.Any(m => m is null))
        {
            throw new ArgumentException("Solution memberships must not contain null.", nameof(solutionMemberships));
        }

        return new CodeProject(
            repositoryId,
            name.Trim(),
            kind,
            ecosystem,
            primaryLanguage,
            projectFilePath,
            rootDirectory,
            copiedProperties.AsReadOnly(),
            memberships);
    }
}
