using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>Identity and version of an analyzer; recorded per run for reproducibility.</summary>
internal sealed record AnalyzerDescriptor
{
    public AnalyzerDescriptor(string id, string version)
    {
        Id = AnalyzerId.Create(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Version = version;
    }

    public AnalyzerId Id { get; }

    public string Version { get; }
}
