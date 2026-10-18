using Minidrive.Application.Common;
using Minidrive.Application.Models;
using FluentValidation;

namespace Minidrive.Application.Validation;

/// <summary>Validates the shape of a move request.</summary>
public sealed class MoveNodeRequestValidator : AbstractValidator<MoveNodeRequest>
{
    public MoveNodeRequestValidator()
    {
        RuleFor(request => request.ParentId)
            .Must(IdFormat.IsValidObjectId)
            .When(request => request.ParentId is not null)
            .WithMessage("ParentId must be a valid identifier.");
    }
}
