using MicroKit.Domain.Rules;

namespace SaasBtp.Access.Domain.Organizations.Rules;

/// <summary>
/// An organization is always identified by a name its founder declared: a blank name leaves the
/// organization unrepresentable, which the founding process forbids ("the context must allow a
/// sufficiently unique representation of the organization").
/// </summary>
/// <param name="displayName">The declared name under test.</param>
public sealed class OrganizationNameMustBeProvidedRule(string? displayName) : BusinessRule
{
    /// <inheritdoc/>
    public override bool IsBroken() => string.IsNullOrWhiteSpace(displayName);

    /// <inheritdoc/>
    public override string Message => "An organization must have a declared name.";
}
