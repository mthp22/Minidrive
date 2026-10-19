using Minidrive.Application.Models;
using FluentValidation;

namespace Minidrive.Application.Validation;

/// <summary>Validates the search query before it reaches the persistence layer.</summary>
public sealed class SearchNodesRequestValidator : AbstractValidator<SearchNodesRequest>
{
    public SearchNodesRequestValidator()
    {
        // NotEmpty also rejects whitespace-only strings for string rules.
        RuleFor(request => request.Q)
            .NotEmpty()
            .WithMessage("A search query is required.")
            .Length(1, 255)
            .WithMessage("The search query must be between 1 and 255 characters long.");
    }
}
