namespace FTMS.SharedKernel.Results;

/// <summary>
/// How a failure should be understood by the outside world.
/// design: doc 05 section 1 - the API maps these onto ProblemDetails status codes in
/// exactly one middleware: NotFound to 404, Validation to 400, Conflict to 409,
/// Failure to 500 with nothing internal leaked.
/// </summary>
public enum ErrorType
{
    /// <summary>Something genuinely unexpected. 500, no detail leaves the process.</summary>
    Failure = 0,

    /// <summary>The requested record does not exist. 404.</summary>
    NotFound = 1,

    /// <summary>The request was malformed or failed a field rule. 400.</summary>
    Validation = 2,

    /// <summary>An illegal state transition or a concurrency clash. 409.</summary>
    Conflict = 3,

    /// <summary>
    /// The caller could not be authenticated. 401.
    ///
    /// Deliberately distinct from <see cref="Failure"/>: a bad password is an expected outcome
    /// of a login attempt, not an internal error, and it must not be logged or reported as one.
    /// design: doc 06 section 3.
    /// </summary>
    Unauthorized = 4,

    /// <summary>
    /// The account exists but is locked out after repeated failures. 423.
    ///
    /// Separate from <see cref="Unauthorized"/> because the user needs to be told to wait rather
    /// than to retype, and because telling them so only ever happens after the password has
    /// already been checked - so it leaks nothing an attacker did not already establish.
    /// </summary>
    Locked = 5,
}

/// <summary>
/// A business failure. Expected outcomes travel as values, not as exceptions.
/// design: doc 03 section 4.
/// </summary>
public record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public ErrorType Type { get; init; } = ErrorType.Failure;

    public static Error NotFound(string code, string message) =>
        new(code, message) { Type = ErrorType.NotFound };

    public static Error Validation(string code, string message) =>
        new(code, message) { Type = ErrorType.Validation };

    public static Error Conflict(string code, string message) =>
        new(code, message) { Type = ErrorType.Conflict };

    public static Error Failure(string code, string message) =>
        new(code, message) { Type = ErrorType.Failure };

    public static Error Unauthorized(string code, string message) =>
        new(code, message) { Type = ErrorType.Unauthorized };

    public static Error Locked(string code, string message) =>
        new(code, message) { Type = ErrorType.Locked };
}

/// <summary>
/// A validation failure carrying the per field messages the client needs to render.
/// design: doc 05 - 400 responses carry an errors dictionary inside ProblemDetails.
/// </summary>
public sealed record ValidationError : Error
{
    public ValidationError(IReadOnlyDictionary<string, string[]> failures)
        : base("validation.failed", "One or more validation errors occurred.")
    {
        Failures = failures;
        Type = ErrorType.Validation;
    }

    public IReadOnlyDictionary<string, string[]> Failures { get; }
}
