using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Domain.Tests.Runs;

public sealed class AnalysisStatisticsTests
{
    [Fact]
    public void Constructor_AcceptsConsistentCounts()
    {
        var statistics = new AnalysisStatistics(100, 5, 3, 10, 50, 7, 2,
            new Dictionary<EvidenceLevel, int> { [EvidenceLevel.Confirmed] = 5, [EvidenceLevel.Syntactic] = 2 },
            new Dictionary<string, TimeSpan> { ["scan"] = TimeSpan.FromSeconds(1) });

        Assert.Equal(5, statistics.RelationshipsByLevel[EvidenceLevel.Confirmed]);
        Assert.Equal(TimeSpan.FromSeconds(1), statistics.StageDurations["scan"]);
    }

    [Fact]
    public void Constructor_RejectsNegativeCounts() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisStatistics(-1, 0, 0, 0, 0, 0, 0));

    [Fact]
    public void Constructor_RejectsLevelCountsNotMatchingTotal() =>
        Assert.Throws<ArgumentException>(() => new AnalysisStatistics(0, 0, 0, 0, 0, 3, 0,
            new Dictionary<EvidenceLevel, int> { [EvidenceLevel.Confirmed] = 2 }));

    [Fact]
    public void Constructor_RejectsNegativeDurations() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisStatistics(0, 0, 0, 0, 0, 0, 0,
            stageDurations: new Dictionary<string, TimeSpan> { ["scan"] = TimeSpan.FromSeconds(-1) }));

    [Fact]
    public void Diagnostic_RejectsBlankCodeOrMessage()
    {
        Assert.ThrowsAny<ArgumentException>(() => new AnalysisDiagnostic(DiagnosticSeverity.Info, " ", "m"));
        Assert.ThrowsAny<ArgumentException>(() => new AnalysisDiagnostic(DiagnosticSeverity.Info, "c", ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisDiagnostic((DiagnosticSeverity)7, "c", "m"));
    }
}
