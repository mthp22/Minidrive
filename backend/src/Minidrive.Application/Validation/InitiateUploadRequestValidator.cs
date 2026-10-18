using Minidrive.Application.Common;
using Minidrive.Application.Models;
using Minidrive.Application.Options;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Minidrive.Application.Validation;

/// <summary>Validates the shape of an upload-initiation request.</summary>
public sealed class InitiateUploadRequestValidator : AbstractValidator<InitiateUploadRequest>
{
    public InitiateUploadRequestValidator(IOptions<UploadOptions> uploadOptions)
    {
        ArgumentNullException.ThrowIfNull(uploadOptions);
        var maxFileSizeBytes = uploadOptions.Value.MaxFileSizeBytes;

        RuleFor(request => request.FileName).ValidNodeName();

        RuleFor(request => request.ParentId)
            .Must(IdFormat.IsValidObjectId)
            .When(request => request.ParentId is not null)
            .WithMessage("ParentId must be a valid identifier.");

        RuleFor(request => request.SizeBytes)
            .GreaterThanOrEqualTo(0)
            .WithMessage("SizeBytes must not be negative.")
            .LessThanOrEqualTo(maxFileSizeBytes)
            .WithMessage($"SizeBytes must not exceed {maxFileSizeBytes} bytes.");

        RuleFor(request => request.ContentType)
            .MaximumLength(255)
            .When(request => request.ContentType is not null);
    }
}
