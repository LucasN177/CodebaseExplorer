using CodebaseExplorer.Infrastructure.Logging;

namespace CodebaseExplorer.IntegrationTests.Infrastructure;

/// <summary>CORE-007: redaction rules of the file log (SECURITY.md §4).</summary>
public sealed class LogRedactorTests
{
    [Theory]
    [InlineData("Cloning https://user:s3cr3t@github.com/org/repo.git", "Cloning https://***@github.com/org/repo.git")]
    [InlineData("remote https://ghtoken@gitlab.com/x", "remote https://***@gitlab.com/x")]
    [InlineData("Authorization: Bearer abc.def.ghi", "Authorization: ***")]
    [InlineData("proxy-authorization=basic dXNlcjpwYXNz", "proxy-authorization=***")]
    [InlineData("GET /api?access_token=abc123&page=2", "GET /api?access_token=***&page=2")]
    [InlineData("api_key=xyz; password: hunter2", "api_key=***; password: ***")]
    [InlineData("token ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ012345 used", "token *** used")]
    [InlineData("key sk-ant-api03-abcdefghijklmnopqrstuvwxyz", "key *** ")]
    public void Secrets_AreMasked(string input, string expected)
    {
        Assert.Equal(expected.TrimEnd(), LogRedactor.Redact(input).TrimEnd());
    }

    [Fact]
    public void OrdinaryMessages_AreUnchanged()
    {
        const string message = @"Import failed with repository.not-found for C:\repos\app (12 projects, https://github.com/org/repo)";

        Assert.Equal(message, LogRedactor.Redact(message));
    }

    [Fact]
    public void LongText_IsTruncated()
    {
        var text = new string('x', LogRedactor.MaxLength + 500);

        var redacted = LogRedactor.Redact(text);

        Assert.StartsWith(new string('x', LogRedactor.MaxLength), redacted, StringComparison.Ordinal);
        Assert.EndsWith("[truncated 500 characters]", redacted, StringComparison.Ordinal);
    }
}
