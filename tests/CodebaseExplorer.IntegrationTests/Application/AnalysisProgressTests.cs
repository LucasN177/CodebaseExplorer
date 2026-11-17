using CodebaseExplorer.Application.Analysis;

namespace CodebaseExplorer.IntegrationTests.Application;

public sealed class AnalysisProgressTests
{
    [Fact]
    public void KnownTotal_ComputesFraction()
    {
        var progress = new AnalysisProgress(AnalysisStage.AnalyzingCode, 25, 100, " src/Billing/Billing.csproj ");

        Assert.Equal(0.25, progress.Fraction);
        Assert.Equal("src/Billing/Billing.csproj", progress.CurrentItem);
    }

    [Fact]
    public void UnknownTotal_HasNoFraction() =>
        Assert.Null(new AnalysisProgress(AnalysisStage.Scanning, 1_000).Fraction);

    [Fact]
    public void EmptyStage_CountsAsComplete() =>
        Assert.Equal(1d, new AnalysisProgress(AnalysisStage.Linking, 0, 0).Fraction);

    [Fact]
    public void BlankCurrentItem_IsNull() =>
        Assert.Null(new AnalysisProgress(AnalysisStage.Scanning, 0, currentItem: "  ").CurrentItem);

    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, -1)]
    [InlineData(11, 10)]
    public void InvalidCounts_Throw(int processed, int? total) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisProgress(AnalysisStage.Scanning, processed, total));

    [Fact]
    public void UndefinedStage_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisProgress((AnalysisStage)42, 0));
}
