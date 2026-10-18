using Minidrive.Application.Common;
using Minidrive.Application.Models;
using FluentValidation;

namespace Minidrive.Application.Validation;

/// <summary>Validates the shape of a create-folder request.</summary>
public sealed class CreateFolderRequestValidator : AbstractValidator<CreateFolderRequest>
{
    public CreateFolderRequestValidator()
    {
        RuleFor(request => request.Name).ValidNodeName();

        RuleFor(request => request.ParentId)
            .Must(IdFormat.IsValidObjectId)
            .When(request => request.ParentId is not null)
            .WithMessage("ParentId must be a valid identifier.");
    }
}
