using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;

namespace CodebaseExplorer.Analysis.Tests.Scanning;

public sealed class FileClassifierTests
{
    private static FileClassification Classify(string path, bool isBinary = false, bool hasGeneratedHeader = false)
    {
        var relativePath = RelativePath.Create(path);
        return FileClassifier.Classify(
            relativePath,
            isBinary,
            hasGeneratedHeader,
            LanguageDetector.Detect(relativePath.FileName, []),
            ProjectMarkerDetector.Detect(relativePath.FileName));
    }

    [Theory]
    [InlineData("tests/Billing.Tests/InvoiceTests.cs", FileClassification.Test)]
    [InlineData("src/Billing.Tests/InvoiceTests.cs", FileClassification.Test)]
    [InlineData("src/Billing.UnitTests/A.cs", FileClassification.Test)]
    [InlineData("web/src/__tests__/app.ts", FileClassification.Test)]
    [InlineData("web/src/app.spec.ts", FileClassification.Test)]
    [InlineData("web/src/app.test.jsx", FileClassification.Test)]
    [InlineData("pkg/test_invoice.py", FileClassification.Test)]
    [InlineData("pkg/invoice_test.py", FileClassification.Test)]
    [InlineData("src/Billing/Invoice.cs", FileClassification.Source)]
    [InlineData("src/latest/a.cs", FileClassification.Source)]
    [InlineData("src/contest.py", FileClassification.Source)]
    [InlineData("web/app.tsx", FileClassification.Source)]
    [InlineData("README.md", FileClassification.Documentation)]
    [InlineData("docs/notes.txt", FileClassification.Documentation)]
    [InlineData("appsettings.json", FileClassification.Configuration)]
    [InlineData("Directory.Build.props", FileClassification.Configuration)]
    [InlineData(".gitignore", FileClassification.Configuration)]
    [InlineData("LICENSE", FileClassification.Other)]
    [InlineData("assets/data.xyz", FileClassification.Other)]
    [InlineData("src/Form1.Designer.cs", FileClassification.Generated)]
    [InlineData("obj/x.g.cs", FileClassification.Generated)]
    [InlineData("api/Client.generated.cs", FileClassification.Generated)]
    [InlineData("tests/Snapshots.g.cs", FileClassification.Generated)]
    public void Classify_ByNameLocationAndLanguage(string path, FileClassification expected) =>
        Assert.Equal(expected, Classify(path));

    [Theory]
    [InlineData("Shop.sln", FileClassification.SolutionFile)]
    [InlineData("Shop.slnx", FileClassification.SolutionFile)]
    [InlineData("src/Billing/Billing.csproj", FileClassification.ProjectManifest)]
    [InlineData("tests/Billing.Tests/Billing.Tests.csproj", FileClassification.ProjectManifest)]
    [InlineData("web/package.json", FileClassification.ProjectManifest)]
    [InlineData("pyproject.toml", FileClassification.ProjectManifest)]
    [InlineData("requirements-dev.txt", FileClassification.ProjectManifest)]
    [InlineData("setup.cfg", FileClassification.ProjectManifest)]
    [InlineData("setup.py", FileClassification.ProjectManifest)]
    public void Markers_AreSolutionFilesOrManifests_EvenInTestFolders(string path, FileClassification expected) =>
        Assert.Equal(expected, Classify(path));

    [Fact]
    public void Binary_TakesPrecedence() =>
        Assert.Equal(FileClassification.Binary, Classify("tests/fixture.g.cs", isBinary: true, hasGeneratedHeader: true));

    [Fact]
    public void GeneratedHeader_TakesPrecedenceOverMarkersAndLanguage()
    {
        Assert.Equal(FileClassification.Generated, Classify("src/Api.cs", hasGeneratedHeader: true));
        Assert.Equal(FileClassification.Generated, Classify("package.json", hasGeneratedHeader: true));
    }
}
