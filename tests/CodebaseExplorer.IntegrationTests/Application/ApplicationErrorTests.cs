using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.IntegrationTests.Application;

public sealed class ApplicationErrorTests
{
    [Theory]
    [InlineData("repository.path-not-found")]
    [InlineData("analysis.already-running")]
    [InlineData("git.auth.token-rejected")]
    [InlineData("io2.x")]
    public void Constructor_AcceptsDottedLowercaseCodes(string code) =>
        Assert.Equal(code, ApplicationError.Validation(code, "message").Code);

    [Theory]
    [InlineData("Repository.PathNotFound")]
    [InlineData("repository..path")]
    [InlineData(".repository")]
    [InlineData("repository.")]
    [InlineData("repository.-path")]
    [InlineData("repository path")]
    [InlineData("repository_path")]
    [InlineData("")]
    public void Constructor_RejectsInvalidCodes(string code) =>
        Assert.ThrowsAny<ArgumentException>(() => ApplicationError.Validation(code, "message"));

    [Fact]
    public void Constructor_RejectsBlankMessageAndUndefinedKind()
    {
        Assert.ThrowsAny<ArgumentException>(() => ApplicationError.Validation("a.b", " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationError((ErrorKind)9, "a.b", "m"));
    }

    [Fact]
    public void Factories_SetKind()
    {
        Assert.Equal(ErrorKind.Validation, ApplicationError.Validation("a", "m").Kind);
        Assert.Equal(ErrorKind.NotFound, ApplicationError.NotFound("a", "m").Kind);
        Assert.Equal(ErrorKind.Conflict, ApplicationError.Conflict("a", "m").Kind);
        Assert.Equal(ErrorKind.Failure, ApplicationError.Failure("a", "m").Kind);
    }

    [Fact]
    public void Details_AreCopiedAndSorted()
    {
        var details = new Dictionary<string, string> { ["path"] = "/repo", ["expected"] = "directory" };

        var error = ApplicationError.Validation("repository.not-a-directory", "Not a directory.", details);
        details["path"] = "changed";

        Assert.Equal(["expected", "path"], error.Details.Keys);
        Assert.Equal("/repo", error.Details["path"]);
    }

    [Fact]
    public void Equality_ComparesDetailContent()
    {
        var first = ApplicationError.NotFound("repository.unknown", "Unknown.", new Dictionary<string, string> { ["id"] = "1" });
        var second = ApplicationError.NotFound("repository.unknown", "Unknown.", new Dictionary<string, string> { ["id"] = "1" });
        var third = ApplicationError.NotFound("repository.unknown", "Unknown.", new Dictionary<string, string> { ["id"] = "2" });

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, third);
    }
}
