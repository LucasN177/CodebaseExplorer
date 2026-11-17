using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;

namespace CodebaseExplorer.Analysis.Tests.Scanning;

public sealed class FileClassifierTests
{
    [Theory]
    [InlineData("tests/Billing.Tests/InvoiceTests.cs", FileClassification.Test)]
    [InlineData("src/Billing.Tests/InvoiceTests.cs", FileClassification.Test)]
    [InlineData("src/Billing.UnitTests/A.cs", FileClassification.Test)]
    [InlineData("web/src/__tests__/app.ts", FileClassification.Test)]
    [InlineData("web/src/app.spec.ts", FileClassification.Test)]
    [InlineData("web/src/app.test.jsx", FileClassification.Test)]
    [InlineData("pkg/test_invoice.py", FileClassification.Test)]
    [InlineData("pkg/invoice_test.py", FileClassification.Test)]
    [InlineData("src/Billing/Invoice.cs", FileClassification.Other)]
    [InlineData("src/latest/a.cs", FileClassification.Other)]
    [InlineData("src/contest.py", FileClassification.Other)]
    [InlineData("src/Form1.Designer.cs", FileClassification.Generated)]
    [InlineData("obj/x.g.cs", FileClassification.Generated)]
    [InlineData("api/Client.generated.cs", FileClassification.Generated)]
    [InlineData("tests/Snapshots.g.cs", FileClassification.Generated)]
    public void Classify_ByNameAndLocation(string path, FileClassification expected) =>
        Assert.Equal(expected, FileClassifier.Classify(RelativePath.Create(path), isBinary: false, hasGeneratedHeader: false));

    [Fact]
    public void Binary_TakesPrecedence() =>
        Assert.Equal(FileClassification.Binary,
            FileClassifier.Classify(RelativePath.Create("tests/fixture.g.cs"), isBinary: true, hasGeneratedHeader: true));

    [Fact]
    public void GeneratedHeader_MarksGenerated() =>
        Assert.Equal(FileClassification.Generated,
            FileClassifier.Classify(RelativePath.Create("src/Api.cs"), isBinary: false, hasGeneratedHeader: true));
}
