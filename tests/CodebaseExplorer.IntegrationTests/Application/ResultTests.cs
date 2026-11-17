using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.IntegrationTests.Application;

public sealed class ResultTests
{
    private static readonly ApplicationError NotFound = ApplicationError.NotFound("repository.unknown", "Unknown repository.");

    [Fact]
    public void Success_HasValueAndNoError()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_HasErrorAndNoValue()
    {
        Result<string> result = NotFound;

        Assert.True(result.IsFailure);
        Assert.Same(NotFound, result.Error);
        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("repository.unknown", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplicitConversion_FromValue_IsSuccess()
    {
        Result<string> result = "ok";

        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public void Success_RejectsNullValue() =>
        Assert.Throws<ArgumentNullException>(() => Result<string>.Success(null!));

    [Fact]
    public void Failure_RejectsNullError() =>
        Assert.Throws<ArgumentNullException>(() => Result<string>.Failure(null!));

    [Fact]
    public void TryGetValue_ReturnsValueOrError()
    {
        Assert.True(Result<int>.Success(1).TryGetValue(out var value, out var noError));
        Assert.Equal(1, value);
        Assert.Null(noError);

        Assert.False(Result<int>.Failure(NotFound).TryGetValue(out _, out var error));
        Assert.Same(NotFound, error);
    }

    [Fact]
    public void Match_CallsMatchingBranch()
    {
        Assert.Equal("value:2", Result<int>.Success(2).Match(v => $"value:{v}", e => e.Code));
        Assert.Equal("repository.unknown", Result<int>.Failure(NotFound).Match(v => $"value:{v}", e => e.Code));
    }

    [Fact]
    public void Map_TransformsSuccessAndPassesFailureThrough()
    {
        Assert.Equal(4, Result<int>.Success(2).Map(v => v * 2).Value);

        var mapped = Result<int>.Failure(NotFound).Map(v => v * 2);
        Assert.Same(NotFound, mapped.Error);
    }

    [Fact]
    public void NonGenericResult_SuccessAndFailure()
    {
        var success = Result.Success();
        Result failure = NotFound;

        Assert.True(success.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => success.Error);
        Assert.True(failure.IsFailure);
        Assert.Same(NotFound, failure.Error);
        Assert.Equal("ok", success.Match(() => "ok", e => e.Code));
        Assert.Equal("repository.unknown", failure.Match(() => "ok", e => e.Code));
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }
}
