namespace CodebaseExplorer.Application.Common;

/// <summary>
/// Outcome of an operation without a value: success, or an expected failure described by an
/// <see cref="ApplicationError"/>. Cancellation is not a result; it surfaces as
/// <see cref="OperationCanceledException"/> (CODING_STANDARDS.md §4).
/// </summary>
public sealed class Result
{
    private static readonly Result SuccessInstance = new(null);

    private readonly ApplicationError? _error;

    private Result(ApplicationError? error) => _error = error;

    public bool IsSuccess => _error is null;

    public bool IsFailure => !IsSuccess;

    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public ApplicationError Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => SuccessInstance;

    public static Result Failure(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static implicit operator Result(ApplicationError error) => Failure(error);

    public TOut Match<TOut>(Func<TOut> onSuccess, Func<ApplicationError, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return _error is null ? onSuccess() : onFailure(_error);
    }

    public override string ToString() => _error is null ? "Success" : $"Failure({_error})";
}
