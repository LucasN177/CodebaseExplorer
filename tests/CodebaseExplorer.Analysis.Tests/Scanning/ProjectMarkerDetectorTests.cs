using CodebaseExplorer.Analysis.Scanning;

namespace CodebaseExplorer.Analysis.Tests.Scanning;

public sealed class ProjectMarkerDetectorTests
{
    [Theory]
    [InlineData("Shop.sln", nameof(ProjectMarker.DotNetSolution))]
    [InlineData("Shop.SLN", nameof(ProjectMarker.DotNetSolution))]
    [InlineData("Shop.slnx", nameof(ProjectMarker.DotNetSolutionXml))]
    [InlineData("Billing.csproj", nameof(ProjectMarker.DotNetProject))]
    [InlineData("Billing.CSPROJ", nameof(ProjectMarker.DotNetProject))]
    [InlineData("Billing.fsproj", nameof(ProjectMarker.DotNetProject))]
    [InlineData("Billing.vbproj", nameof(ProjectMarker.DotNetProject))]
    [InlineData("package.json", nameof(ProjectMarker.NodePackage))]
    [InlineData("pyproject.toml", nameof(ProjectMarker.PythonProject))]
    [InlineData("requirements.txt", nameof(ProjectMarker.PythonRequirements))]
    [InlineData("requirements-dev.txt", nameof(ProjectMarker.PythonRequirements))]
    [InlineData("requirements_test.txt", nameof(ProjectMarker.PythonRequirements))]
    [InlineData("setup.cfg", nameof(ProjectMarker.PythonSetupConfig))]
    [InlineData("setup.py", nameof(ProjectMarker.PythonSetupScript))]
    public void Detect_RecognizesMarkers(string fileName, string expected) =>
        Assert.Equal(Enum.Parse<ProjectMarker>(expected), ProjectMarkerDetector.Detect(fileName));

    [Theory]
    [InlineData("package-lock.json")]
    [InlineData("Package.json")]
    [InlineData("tsconfig.json")]
    [InlineData("Directory.Build.props")]
    [InlineData("requirements.in")]
    [InlineData("dev-requirements.txt")]
    [InlineData("setup.py.bak")]
    [InlineData("A.cs")]
    [InlineData("csproj")]
    public void Detect_OtherFiles_AreNoMarkers(string fileName) =>
        Assert.Null(ProjectMarkerDetector.Detect(fileName));

    [Fact]
    public void IsSolution_OnlyForSolutionMarkers()
    {
        Assert.True(ProjectMarker.DotNetSolution.IsSolution());
        Assert.True(ProjectMarker.DotNetSolutionXml.IsSolution());
        Assert.False(ProjectMarker.DotNetProject.IsSolution());
        Assert.False(ProjectMarker.NodePackage.IsSolution());
    }
}
