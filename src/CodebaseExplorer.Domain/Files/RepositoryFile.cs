using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Files;

/// <summary>A scanned file of a repository (DOMAIN_MODEL.md §4.3).</summary>
public sealed class RepositoryFile
{
    private RepositoryFile(
        RepositoryId repositoryId,
        RelativePath path,
        LanguageId language,
        FileClassification classification,
        long sizeBytes,
        ContentHash hash,
        ElementId? projectId,
        int? lineCount)
    {
        NaturalKey = IdentityKey(repositoryId, path);
        Id = ElementId.FromNaturalKey(NaturalKey);
        RepositoryId = repositoryId;
        Path = path;
        Language = language;
        Classification = classification;
        SizeBytes = sizeBytes;
        Hash = hash;
        ProjectId = projectId;
        LineCount = lineCount;
    }

    public ElementId Id { get; }

    public NaturalKey NaturalKey { get; }

    public RepositoryId RepositoryId { get; }

    public RelativePath Path { get; }

    public LanguageId Language { get; }

    public FileClassification Classification { get; }

    public long SizeBytes { get; }

    public ContentHash Hash { get; }

    /// <summary>Owning project (innermost project root containing the file), if any.</summary>
    public ElementId? ProjectId { get; }

    /// <summary>Number of lines for text files; <see langword="null"/> for binary or unread files.</summary>
    public int? LineCount { get; }

    /// <exception cref="ArgumentException">An argument violates a file invariant.</exception>
    public static RepositoryFile Create(
        RepositoryId repositoryId,
        RelativePath path,
        LanguageId language,
        FileClassification classification,
        long sizeBytes,
        ContentHash hash,
        ElementId? projectId = null,
        int? lineCount = null)
    {
        if (repositoryId.Value == Guid.Empty)
        {
            throw new ArgumentException("A file requires a repository id.", nameof(repositoryId));
        }

        if (path.IsRoot)
        {
            throw new ArgumentException("A file path must not be the repository root.", nameof(path));
        }

        if (!Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification), classification, null);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        ArgumentNullException.ThrowIfNull(hash);

        if (lineCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount), lineCount, "The line count must not be negative.");
        }

        return new RepositoryFile(repositoryId, path, language, classification, sizeBytes, hash, projectId, lineCount);
    }

    /// <summary>The id a file at <paramref name="path"/> has, without creating the file.</summary>
    public static ElementId IdFor(RepositoryId repositoryId, RelativePath path) =>
        ElementId.FromNaturalKey(IdentityKey(repositoryId, path));

    private static NaturalKey IdentityKey(RepositoryId repositoryId, RelativePath path) =>
        NaturalKey.ForFile(repositoryId, path.Value);
}
