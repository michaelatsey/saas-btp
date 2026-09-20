using MicroKit.Domain.Rules;
using SaasBtp.Access.Domain.Identity;

namespace SaasBtp.Access.Domain.Organizations.Rules;

/// <summary>
/// An organization is founded by exactly one identified owner (INV-2, INV-3). The "exactly one" half
/// is guaranteed STRUCTURALLY — the founding factory establishes the ownership itself and exposes no
/// way to add a second, so an ownerless or twice-owned organization is unrepresentable. This rule
/// guards the half a type cannot: that the owner is actually somebody.
/// </summary>
/// <param name="owner">The founder's identity under test.</param>
public sealed class OrganizationMustHaveAnIdentifiedOwnerRule(PersonId owner) : BusinessRule
{
    /// <inheritdoc/>
    public override bool IsBroken() => owner.IsUnidentified;

    /// <inheritdoc/>
    public override string Message => "An organization must be founded by an identified owner.";
}
