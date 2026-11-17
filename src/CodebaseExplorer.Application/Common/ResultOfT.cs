using System.Diagnostics.CodeAnalysis;

namespace CodebaseExplorer.Application.Common;

/// <summary>
/// Outcome of an operation that produces a <typeparamref name="T"/> on success, or an expected
/// failure described by an <see cref="ApplicationError"/>. Cancellation surfaces as
/// <see cref="OperationCanceledException"/>, not as a failure.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly ApplicationError? _error;

    private Result(T? value, ApplicationError? error)
    {
        _value = value;
        _error = error;
    }

    public bool IsSuccess => _error is null;

    public bool IsFailure => !IsSuccess;

    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"A failed result has no value ({_error}).");

    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public ApplicationError Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(value, null);
    }

    public static Result<T> Failure(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(ApplicationError error) => Failure(error);

    /// <summary>Returns the value on success; otherwise the error.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out ApplicationError error)
    {
        value = _value;
        error = _error;
        return _error is null;
    }

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<ApplicationError, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return _error is null ? onSuccess(_value!) : onFailure(_error);
    }

    /// <summary>Transforms the value of a success; a failure is passed through unchanged.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return _error is null ? Result<TOut>.Success(map(_value!)) : Result<TOut>.Failure(_error);
    }

    public override string ToString() => _error is null ? $"Success({_value})" : $"Failure({_error})";
}
