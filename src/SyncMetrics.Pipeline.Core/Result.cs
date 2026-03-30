namespace SyncMetrics.Pipeline.Core;

/// <summary>
/// Represents the outcome of an operation that can succeed with a value or fail with a PipelineError.
/// Inspired by Railway-Oriented Programming — errors are values, not exceptions.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly PipelineError? _error;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(PipelineError error)
    {
        _error = error ?? throw new ArgumentNullException(nameof(error));
        IsSuccess = false;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed Result. Check IsSuccess first.");

    public PipelineError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("Cannot access Error on a successful Result. Check IsFailure first.");

    #pragma warning disable CA1000 // Factory methods on generic types are idiomatic for Result<T>
    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(PipelineError error) => new(error);
    #pragma warning restore CA1000

    /// <summary>
    /// Chains a fallible operation. If this Result is Success, applies <paramref name="func"/> to the value.
    /// If this Result is Failure, propagates the error without calling <paramref name="func"/>.
    /// </summary>
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> func) =>
        IsSuccess ? func(_value!) : Result<TNext>.Failure(_error!);

    /// <summary>
    /// Transforms the success value. If this Result is Failure, propagates the error.
    /// </summary>
    public Result<TNext> Map<TNext>(Func<T, TNext> func) =>
        IsSuccess ? Result<TNext>.Success(func(_value!)) : Result<TNext>.Failure(_error!);

    /// <summary>
    /// Returns the value if successful, or <paramref name="defaultValue"/> if failed.
    /// </summary>
    public T GetValueOrDefault(T defaultValue) =>
        IsSuccess ? _value! : defaultValue;
}
