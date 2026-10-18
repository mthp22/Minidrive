using Minidrive.Domain.Exceptions;
using Minidrive.Domain.ValueObjects;
using FluentValidation;

namespace Minidrive.Application.Validation;

/// <summary>
/// Shared node-name rule. It delegates to the domain value object so request validation and domain
/// invariants can never drift apart.
/// </summary>
public static class NodeNameValidationExtensions
{
    public static IRuleBuilderOptions<T, string> ValidNodeName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder.Must(IsValidNodeName)
            .WithMessage(
                "The name is invalid. Use 1-255 characters, avoid '/' and '\\', and do not use '.' or '..'.");
    }

    private static bool IsValidNodeName(string? value)
    {
        try
        {
            NodeName.Validate(value);
            return true;
        }
        catch (InvalidNodeNameException)
        {
            return false;
        }
    }
}
