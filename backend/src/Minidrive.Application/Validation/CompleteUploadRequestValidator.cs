using Minidrive.Application.Models;
using FluentValidation;

namespace Minidrive.Application.Validation;

/// <summary>
/// Validates the shape of a completion request. Ordering, contiguity and expected part count are
/// enforced by <c>MultipartPartRules</c> against the persisted session, so they are intentionally
/// not duplicated here.
/// </summary>
public sealed class CompleteUploadRequestValidator : AbstractValidator<CompleteUploadRequest>
{
    public CompleteUploadRequestValidator()
    {
        RuleFor(request => request.Parts)
            .NotNull()
            .Must(parts => parts is { Count: > 0 })
            .WithMessage("At least one part is required.");

        RuleForEach(request => request.Parts)
            .ChildRules(part =>
            {
                part.RuleFor(p => p.PartNumber)
                    .GreaterThanOrEqualTo(1)
                    .WithMessage("Part numbers start at 1.");

                part.RuleFor(p => p.ETag)
                    .NotEmpty()
                    .WithMessage("An ETag is required for every part.");
            });
    }
}
