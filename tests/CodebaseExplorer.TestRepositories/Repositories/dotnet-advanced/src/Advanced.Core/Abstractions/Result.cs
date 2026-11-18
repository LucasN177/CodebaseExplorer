namespace Advanced.Core.Abstractions;

public enum ResultKind
{
    Success,
    Failure,
}

public readonly struct Result<T>
{
    public Result(ResultKind kind, T? value)
    {
        Kind = kind;
        Value = value;
    }

    public ResultKind Kind { get; }

    public T? Value { get; }

    public static Result<T> Ok(T value) => new(ResultKind.Success, value);
}

public delegate void ResultHandler<T>(Result<T> result);
