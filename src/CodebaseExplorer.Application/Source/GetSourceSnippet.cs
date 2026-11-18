using System.Security.Cryptography;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Source;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.Source;

/// <summary>
/// Use case: the source excerpt around an evidence location (UI-007, USER_WORKFLOWS.md WF-04): the evidence lines
/// plus <see cref="ContextLines"/> lines before and after, read through <see cref="IRepositoryFileReader"/>
/// (containment, size limit). Only files of the repository's current model can be read.
/// </summary>
/// <remarks>
/// Missing files and folders are reported as errors; a file whose content differs from the analyzed version is still
/// shown, flagged with <see cref="SourceSnippetDto.ChangedSinceAnalysis"/>. Snippet text is never logged.
/// </remarks>
public sealed class GetSourceSnippet(IArchitectureModelStore store, IRepositoryRootValidator rootValidator, IRepositoryFileReader fileReader)
{
    public const int ContextLines = 5;

    /// <summary>Lines longer than this are shortened for display.</summary>
    public const int MaxLineLength = 500;

    public async Task<Result<SourceSnippetDto>> ExecuteAsync(
        string repositoryId, string path, int startLine, int? endLine, CancellationToken cancellationToken)
    {
        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (startLine < 1 || endLine < startLine)
        {
            return ApplicationError.Validation(SourceErrorCodes.RangeInvalid, "The line range is not valid.");
        }

        if (await store.LoadCurrentModelAsync(id, cancellationToken) is not { } stored)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotAnalyzed, "The repository has not been analyzed yet.");
        }

        // Only files the analysis saw: the path comes from the UI, but must never reach arbitrary files.
        var file = stored.Model.Files.FirstOrDefault(f => string.Equals(f.Path.Value, path, StringComparison.Ordinal));
        if (file is null)
        {
            return ApplicationError.NotFound(SourceErrorCodes.FileNotInModel, "The file is not part of the analyzed repository.");
        }

        if (!rootValidator.Validate(stored.Repository.Location.LocalPath).TryGetValue(out var root, out var rootError))
        {
            return ApplicationError.NotFound(SourceErrorCodes.FolderMissing,
                $"The repository folder is not available any more ({rootError.Message}). Re-analyze or remove the repository.");
        }

        var limit = AnalysisLimits.Default.MaxParseFileBytes;
        var bytes = await fileReader.ReadAllBytesAsync(root, file.Path, limit, cancellationToken);
        if (!bytes.TryGetValue(out var content, out var readError))
        {
            return readError.Code == RepositoryErrorCodes.FileNotFound
                ? ApplicationError.NotFound(SourceErrorCodes.FileMissing, $"{file.Path} does not exist any more. Re-analyze the repository.")
                : ApplicationError.Failure(SourceErrorCodes.FileUnreadable, $"{file.Path} cannot be shown: {readError.Message}");
        }

        var text = await fileReader.ReadAllTextAsync(root, file.Path, limit, cancellationToken);
        if (!text.TryGetValue(out var source, out readError))
        {
            return ApplicationError.Failure(SourceErrorCodes.FileUnreadable, $"{file.Path} cannot be shown: {readError.Message}");
        }

        var lines = source.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1]; // trailing line break
        }

        var last = endLine ?? startLine;
        var first = Math.Max(1, startLine - ContextLines);
        var end = Math.Min(lines.Length, last + ContextLines);
        if (startLine > lines.Length)
        {
            return ApplicationError.NotFound(SourceErrorCodes.LinesMissing,
                $"{file.Path} has only {lines.Length} lines now; the evidence (line {startLine}) is outside. Re-analyze the repository.");
        }

        return new SourceSnippetDto
        {
            Path = file.Path.Value,
            AbsolutePath = Path.Combine(root.FullPath, file.Path.Value.Replace('/', Path.DirectorySeparatorChar)),
            Lines = [.. Enumerable.Range(first, end - first + 1).Select(number => new SourceLineDto(
                number, Display(lines[number - 1]), number >= startLine && number <= last))],
            ChangedSinceAnalysis = ContentHash.FromBytes(SHA256.HashData(content)) != file.Hash,
            RangeTruncated = last > lines.Length,
        };
    }

    private static string Display(string line)
    {
        var text = line.TrimEnd('\r');
        return text.Length <= MaxLineLength ? text : string.Concat(text.AsSpan(0, MaxLineLength), " …");
    }
}
