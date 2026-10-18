using Minidrive.Application.Models;
using FluentValidation;

namespace Minidrive.Application.Validation;

/// <summary>Validates the shape of a rename (PATCH) request.</summary>
public sealed class RenameNodeRequestValidator : AbstractValidator<RenameNodeRequest>
{
    public RenameNodeRequestValidator()
    {
        RuleFor(request => request.Name).ValidNodeName();
    }
}
