using CodebaseExplorer.Domain.Files;

namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>Files</c> (§4.4).</summary>
internal sealed class FileRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public string RelativePath { get; set; } = string.Empty;

    public string NaturalKey { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public FileClassification Classification { get; set; }

    public long SizeBytes { get; set; }

    public int? LineCount { get; set; }

    public byte[] ContentHash { get; set; } = [];

    public Guid? ProjectId { get; set; }

    public Guid LastSeenRunId { get; set; }
}
