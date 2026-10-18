using FluentValidation;
using Minidrive.Application.Exceptions;

namespace Minidrive.Api.Validation;

/// <summary>
/// Runs a FluentValidation validator at the HTTP boundary and converts failures into the
/// application's <see cref="ValidationFailedException"/>, so endpoint validation and service-level
/// validation produce identical Problem Details payloads.
/// </summary>
public static class ValidatorExtensions
{
    public static async Task ValidateRequestAsync<TRequest>(
        this IValidator<TRequest> validator,
        TRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = await validator.ValidateAsync(request, cancellationToken);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(error => Camelize(error.PropertyName))
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        throw new ValidationFailedException(errors);
    }

    /// <summary>
    /// Property names arrive as CLR names (<c>FileName</c>) but the JSON contract is camelCase, so
    /// reported field names match what the client actually sent.
    /// </summary>
    private static string Camelize(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return "request";
        }

        return char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
    }
}
