using System.Security.Cryptography;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;

namespace CodebaseExplorer.AI.Retrieval;

/// <summary>
/// Reads line ranges of analyzed files for one retrieval, through <see cref="IRepositoryFileReader"/>. A file is used only
/// if <see cref="SecretFilePolicy"/> allows it and its content still has the analyzed hash, so snippet line numbers match
/// the model. Files are read at most once per retrieval.
/// </summary>
internal sealed class SnippetSource(RepositoryRoot? root, IRepositoryFileReader reader)
{
    /// <summary>A snippet never has more lines than this (AI_ARCHITECTURE.md §4).</summary>
    public const int MaxSnippetLines = 60;

    private readonly Dictionary<ElementId, string[]?> _lines = [];

    /// <summary>The lines <paramref name="startLine"/>…<paramref name="endLine"/> (cut to <see cref="MaxSnippetLines"/>), or null.</summary>
    public async Task<(int Start, int End, string Text)?> ReadAsync(RepositoryFile file, int startLine, int endLine, CancellationToken cancellationToken)
    {
        if (root is null || !SecretFilePolicy.IsAllowed(file))
        {
            return null;
        }

        if (!_lines.TryGetValue(file.Id, out var lines))
        {
            lines = await LoadAsync(root, file, cancellationToken);
            _lines[file.Id] = lines;
        }

        if (lines is null || startLine < 1 || startLine > lines.Length)
        {
            return null;
        }

        var end = Math.Min(Math.Min(Math.Max(startLine, endLine), lines.Length), startLine + MaxSnippetLines - 1);
        return (startLine, end, string.Join('\n', lines[(startLine - 1)..end]));
    }

    private async Task<string[]?> LoadAsync(RepositoryRoot repositoryRoot, RepositoryFile file, CancellationToken cancellationToken)
    {
        var limit = AnalysisLimits.Default.MaxParseFileBytes;
        if (!(await reader.ReadAllBytesAsync(repositoryRoot, file.Path, limit, cancellationToken)).TryGetValue(out var bytes, out _)
            || ContentHash.FromBytes(SHA256.HashData(bytes)) != file.Hash
            || !(await reader.ReadAllTextAsync(repositoryRoot, file.Path, limit, cancellationToken)).TryGetValue(out var text, out _))
        {
            return null;
        }

        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        return [.. lines.Select(line =>
        {
            var trimmed = line.TrimEnd('\r');
            return trimmed.Length <= SourceChunker.MaxLineLength ? trimmed : trimmed[..SourceChunker.MaxLineLength];
        })];
    }
}
