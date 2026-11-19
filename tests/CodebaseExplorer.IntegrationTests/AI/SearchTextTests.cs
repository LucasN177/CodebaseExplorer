using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>AI-003: search words, chunking and the secret-file policy.</summary>
public sealed class SearchTextTests
{
    [Fact]
    public void QueryTerms_SplitIdentifiers_AndDropStopWords()
    {
        Assert.Equal(
            ["invoiceservice", "invoice", "service", "calculate", "totals", "max_retry", "max", "retry"],
            SearchTerms.ForQuery("How does the InvoiceService calculate totals? What is MAX_RETRY?"));
    }

    [Fact]
    public void QueryTerms_AreLimited_AndContainNoSyntax()
    {
        var terms = SearchTerms.ForQuery(string.Join(' ', Enumerable.Range(0, 100).Select(i => $"word{i}")) + " \"x\" OR NEAR(* ) -y");

        Assert.Equal(SearchTerms.MaxQueryTerms, terms.Count);
        Assert.All(terms, t => Assert.True(t.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'), t));
    }

    [Theory]
    [InlineData("orders", "order")]
    [InlineData("order", "order")]
    [InlineData("placed", "plac")]
    [InlineData("place", "plac")]
    [InlineData("repositories", "repository")]
    [InlineData("classes", "class")]
    [InlineData("handling", "handl")]
    [InlineData("is", "is")]
    public void Stems_LetWordFormsMeet(string word, string stem) => Assert.Equal(stem, SearchTerms.Stem(word));

    [Fact]
    public void Keywords_ArePartsOfCompoundIdentifiers()
    {
        Assert.Equal("http client factory user id",SearchTerms.KeywordsOf("var c = HttpClientFactory.Get(userId); plain words"));
    }

    [Fact]
    public void Chunks_OverlapAndKeepLineNumbers()
    {
        var file = new ElementId(Guid.NewGuid());
        var text = string.Join("\r\n", Enumerable.Range(1, 75).Select(i => $"line {i}")) + "\r\n";

        var chunks = SourceChunker.Chunk(file, text);

        Assert.Equal([(1, 40), (31, 70), (61, 75)], chunks.Select(c => (c.StartLine, c.EndLine)));
        Assert.StartsWith("line 31\nline 32", chunks[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', chunks[0].Text);
    }

    [Fact]
    public void Chunks_SkipBlankWindows_AndCutLongLines()
    {
        var file = new ElementId(Guid.NewGuid());
        var text = new string('\n', 80) + new string('x', 1000);

        var chunk = Assert.Single(SourceChunker.Chunk(file, text));

        Assert.Equal(61, chunk.StartLine);
        Assert.Equal(81, chunk.EndLine);
        Assert.EndsWith(new string('x', SourceChunker.MaxLineLength), chunk.Text, StringComparison.Ordinal);
        Assert.Empty(SourceChunker.Chunk(file, string.Empty));
    }

    [Theory]
    [InlineData(".env", true)]
    [InlineData("config/.env.production", true)]
    [InlineData("certs/server.PEM", true)]
    [InlineData("deploy/id_rsa", true)]
    [InlineData("deploy/id_ed25519.pub", true)]
    [InlineData("src/Web/appsettings.Production.json", true)]
    [InlineData("src/Web/appsettings.json", false)]
    [InlineData(".npmrc", true)]
    [InlineData("src/environment.ts", false)]
    [InlineData("src/keys.cs", false)]
    public void SecretNames_AreRecognized(string path, bool secret) => Assert.Equal(secret, SecretFilePolicy.IsSecretName(path));
}
