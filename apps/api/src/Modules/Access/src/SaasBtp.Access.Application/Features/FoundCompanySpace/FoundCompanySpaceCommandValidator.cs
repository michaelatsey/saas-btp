using FluentValidation;

namespace SaasBtp.Access.Application.Features.FoundCompanySpace;

/// <summary>
/// Input validation for the founding intent — declarative, opt-in per request (cqrs-mediatr rule),
/// executed by the module's ValidationBehavior before the handler runs. Lengths mirror the DbUp
/// schema (email 255, names 200) so malformed input fails as a validation Result, not as a database
/// error deep inside the founding transaction.
/// </summary>
public sealed class FoundCompanySpaceCommandValidator : AbstractValidator<FoundCompanySpaceCommand>
{
    /// <summary>Initializes the validation rules.</summary>
    public FoundCompanySpaceCommandValidator()
    {
        RuleFor(command => command.FounderEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(255);

        RuleFor(command => command.FounderFullName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(command => command.OrganizationDisplayName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(command => command.WorkspaceDisplayName)
            .NotEmpty()
            .MaximumLength(200);
    }
}
