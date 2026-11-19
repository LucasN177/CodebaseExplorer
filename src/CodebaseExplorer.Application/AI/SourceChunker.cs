using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Splits a file into overlapping line windows for the search index (AI-003): <see cref="ChunkLines"/> lines per chunk,
/// each starting <see cref="StepLines"/> after the previous one, so a definition near a border appears whole in one chunk.
/// Blank chunks are dropped; long lines are shortened.
/// </summary>
public static class SourceChunker
{
    public const int ChunkLines = 40;

    public const int StepLines = 30;

    /// <summary>Lines longer than this are cut (minified code would otherwise dominate a chunk).</summary>
    public const int MaxLineLength = 400;

    public static IReadOnlyList<SearchChunk> Chunk(ElementId fileId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        var chunks = new List<SearchChunk>();
        for (var start = 0; start < lines.Length; start += StepLines)
        {
            var end = Math.Min(lines.Length, start + ChunkLines);
            var window = lines[start..end].Select(line =>
            {
                var trimmed = line.TrimEnd('\r');
                return trimmed.Length <= MaxLineLength ? trimmed : trimmed[..MaxLineLength];
            });
            var chunkText = string.Join('\n', window);
            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                chunks.Add(new SearchChunk(fileId, start + 1, end, chunkText, SearchTerms.KeywordsOf(chunkText)));
            }

            if (end == lines.Length)
            {
                break;
            }
        }

        return chunks;
    }
}
