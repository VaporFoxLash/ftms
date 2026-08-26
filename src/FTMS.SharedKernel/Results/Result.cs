namespace FTMS.SharedKernel.Results;

/// <summary>
/// The outcome of an operation that is allowed to fail for business reasons.
/// design: doc 03 section 4 - a missing transaction, an illegal status transition and a
/// validation miss are expected outcomes, so they are values. Exceptions stay reserved
/// for the genuinely exceptional: the database is down, or there is a bug.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>A <see cref="Result"/> that carries a value when it succeeds.</summary>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>
    /// The value. Reading it on a failed result is a programming error, not a business
    /// outcome, so this is one of the few places FTMS throws.
    /// </summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be read.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
