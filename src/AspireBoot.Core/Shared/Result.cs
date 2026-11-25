namespace AspireBoot.Core.Shared;

/// <summary>A stable, translatable failure code plus a developer-facing description.</summary>
public sealed record Failure(string Code, string Description);

/// <summary>Outcome of an operation whose failure is expected (validation, business rules).</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    internal Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    internal Result(Failure failure) => Failure = failure;

    public bool IsSuccess { get; }

    public Failure? Failure { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot read the value of a failed result.");
}

/// <summary>Outcome of an operation without a value whose failure is expected (validation, business rules).</summary>
public sealed class Result
{
    private Result(Failure? failure)
    {
        Failure = failure;
        IsSuccess = failure is null;
    }

    public bool IsSuccess { get; }

    public Failure? Failure { get; }

    public static Result Success() => new(null);

    public static Result Fail(Failure failure) => new(failure ?? throw new ArgumentNullException(nameof(failure)));

    public static Result<T> Success<T>(T value) => new(value);

    public static Result<T> Fail<T>(Failure failure) => new(failure);
}