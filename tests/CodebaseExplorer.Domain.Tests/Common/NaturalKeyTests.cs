using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Tests.Common;

public sealed class NaturalKeyTests
{
    private static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c6d7"));
    private const string RepositoryHex = "0193a1b2c3d47e5f80718293a4b5c6d7";

    [Fact]
    public void ForFile_UsesDocumentedFormat()
    {
        var key = NaturalKey.ForFile(Repository, "src/Billing/Invoice.cs");

        Assert.Equal($"file:{RepositoryHex}:src/Billing/Invoice.cs", key.Value);
    }

    [Theory]
    [InlineData(@"src\Billing\Invoice.cs")]
    [InlineData("./src/Billing/Invoice.cs")]
    [InlineData("././src//Billing/Invoice.cs")]
    [InlineData("src/Billing/Invoice.cs/")]
    public void ForFile_NormalizesPathVariants(string path)
    {
        var key = NaturalKey.ForFile(Repository, path);

        Assert.Equal($"file:{RepositoryHex}:src/Billing/Invoice.cs", key.Value);
    }

    [Fact]
    public void ForFile_PreservesCase()
    {
        var upper = NaturalKey.ForFile(Repository, "Src/A.cs");
        var lower = NaturalKey.ForFile(Repository, "src/a.cs");

        Assert.NotEqual(upper, lower);
    }

    [Fact]
    public void ForFile_EscapesPercentAndColonInPaths()
    {
        var key = NaturalKey.ForFile(Repository, "docs/50%:done.md");

        Assert.Equal($"file:{RepositoryHex}:docs/50%25%3Adone.md", key.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("./")]
    [InlineData("//")]
    public void ForFile_EmptyPath_Throws(string path) =>
        Assert.ThrowsAny<ArgumentException>(() => NaturalKey.ForFile(Repository, path));

    [Fact]
    public void ForProject_UsesDocumentedFormat()
    {
        var key = NaturalKey.ForProject(Repository, "dotnet", @"src\Billing\Billing.csproj");

        Assert.Equal($"project:{RepositoryHex}:dotnet:src/Billing/Billing.csproj", key.Value);
    }

    [Theory]
    [InlineData("DotNet")]
    [InlineData("dot net")]
    [InlineData("dot:net")]
    [InlineData("")]
    public void ForProject_InvalidEcosystemToken_Throws(string token) =>
        Assert.ThrowsAny<ArgumentException>(() => NaturalKey.ForProject(Repository, token, "a.csproj"));

    [Fact]
    public void ForModule_EmbedsProjectKey()
    {
        var project = NaturalKey.ForProject(Repository, "dotnet", "src/Billing/Billing.csproj");

        var key = NaturalKey.ForModule(project, "namespace", "Billing.Invoices");

        Assert.Equal($"module:project:{RepositoryHex}:dotnet:src/Billing/Billing.csproj:namespace:Billing.Invoices", key.Value);
    }

    [Fact]
    public void ForModule_ColonInProjectPath_CannotCollideWithDifferentSplit()
    {
        // Without escaping, project path "a:namespace" + qualified name "X" and project path "a"
        // + qualified name "X" with a crafted kind could render to the same string.
        var projectWithColon = NaturalKey.ForProject(Repository, "node", "a:namespace");
        var plainProject = NaturalKey.ForProject(Repository, "node", "a");

        var first = NaturalKey.ForModule(projectWithColon, "directory", "X");
        var second = NaturalKey.ForModule(plainProject, "namespace", "directory:X");

        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public void ForModule_NonProjectKey_Throws()
    {
        var fileKey = NaturalKey.ForFile(Repository, "a.cs");

        Assert.Throws<ArgumentException>(() => NaturalKey.ForModule(fileKey, "namespace", "A"));
    }

    [Fact]
    public void ForEntity_KeepsSymbolKeyVerbatim()
    {
        var project = NaturalKey.ForProject(Repository, "dotnet", "src/Billing/Billing.csproj");

        var key = NaturalKey.ForEntity(project, "csharp", "M:Billing.InvoiceService.Create(System.String)");

        Assert.Equal(
            $"entity:project:{RepositoryHex}:dotnet:src/Billing/Billing.csproj:csharp:M:Billing.InvoiceService.Create(System.String)",
            key.Value);
    }

    [Fact]
    public void ForEntity_SymbolKeyWithLineBreak_Throws()
    {
        var project = NaturalKey.ForProject(Repository, "dotnet", "a.csproj");

        Assert.Throws<ArgumentException>(() => NaturalKey.ForEntity(project, "csharp", "T:A\nB"));
    }

    [Theory]
    [InlineData("nuget", "Serilog.Sinks.Console", "serilog.sinks.console")]
    [InlineData("dotnet-framework", "Microsoft.AspNetCore.App", "microsoft.aspnetcore.app")]
    [InlineData("pypi", "Friendly-Bard", "friendly-bard")]
    [InlineData("pypi", "Friendly_Bard", "friendly-bard")]
    [InlineData("pypi", "friendly.bard", "friendly-bard")]
    [InlineData("pypi", "FRIENDLY-._-BARD", "friendly-bard")]
    [InlineData("npm", "@scope/Package", "@scope/Package")]
    [InlineData("npm", " react ", "react")]
    public void ForExternalDependency_NormalizesNamePerEcosystem(string ecosystem, string name, string expectedName)
    {
        var key = NaturalKey.ForExternalDependency(Repository, ecosystem, name);

        Assert.Equal($"extdep:{RepositoryHex}:{ecosystem}:{expectedName}", key.Value);
    }

    [Fact]
    public void ForRelationship_UsesElementIds()
    {
        var source = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("a"));
        var target = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("b"));

        var key = NaturalKey.ForRelationship(source, "inherits", target);

        Assert.Equal($"rel:{source}:inherits:{target}", key.Value);
    }

    [Fact]
    public void ForEvidence_WithLocation_UsesDocumentedFormat()
    {
        var relationship = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("rel"));
        var file = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("file"));

        var key = NaturalKey.ForEvidence(relationship, "dotnet.msbuild-static", "csproj.ProjectReference", file, 12, 5);

        Assert.Equal($"evidence:{relationship}:dotnet.msbuild-static:csproj.ProjectReference:{file}:12:5", key.Value);
    }

    [Fact]
    public void ForEvidence_WithoutLocation_UsesPlaceholder()
    {
        var relationship = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("rel"));

        var key = NaturalKey.ForEvidence(relationship, "git.libgit2", "git.cochange", fileId: null, 0, 0);

        Assert.Equal($"evidence:{relationship}:git.libgit2:git.cochange:-:0:0", key.Value);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void ForEvidence_NonPositiveLocation_Throws(int line, int column)
    {
        var relationship = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("rel"));
        var file = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("file"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NaturalKey.ForEvidence(relationship, "csharp.roslyn", "csharp.semantic.BaseType", file, line, column));
    }

    [Theory]
    [InlineData("rule:with:colons")]
    [InlineData("rule with space")]
    public void ForEvidence_InvalidRule_Throws(string rule)
    {
        var relationship = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("rel"));

        Assert.Throws<ArgumentException>(() =>
            NaturalKey.ForEvidence(relationship, "csharp.roslyn", rule, fileId: null, 0, 0));
    }

    [Fact]
    public void FromCanonicalString_RoundTripsValue()
    {
        var original = NaturalKey.ForFile(Repository, "a/b.cs");

        Assert.Equal(original, NaturalKey.FromCanonicalString(original.Value));
    }
}
