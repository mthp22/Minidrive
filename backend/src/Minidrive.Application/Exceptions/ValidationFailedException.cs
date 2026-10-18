namespace Minidrive.Application.Exceptions;

/// <summary>
/// Thrown when request validation fails. Carries per-field messages so the API can surface a
/// consistent validation Problem Details payload.
/// </summary>
public sealed class ValidationFailedException : AppException
{
    public const string ErrorCode = "request.validation";

    public ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
        : base(ErrorCode, "One or more validation errors occurred.")
    {
        Errors = errors;
    }

    /// <summary>Field name → messages.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
