using MicroKit.Domain.Rules;
using SaasBtp.Access.Domain.Identity;

namespace SaasBtp.Access.Domain.Organizations.Rules;

/// <summary>
/// Belonging is BINARY: a person is part of an organization's collective, or is not. There is no
/// second membership to hold, so admitting someone who already belongs is refused.
/// </summary>
/// <remarks>
/// This rule is why the memberships live inside the <see cref="Organization"/> aggregate: deciding
/// it requires seeing the organization AND all its memberships at once. A model that scattered the
/// memberships outside the aggregate could only delegate this to a database unique index — which
/// makes the database, not the model, the place the business rule is written down.
/// </remarks>
/// <param name="members">The people who already belong.</param>
/// <param name="candidate">The person about to be admitted.</param>
public sealed class MembershipMustBeUniquePerPersonRule(
    IEnumerable<PersonId> members, PersonId candidate) : BusinessRule
{
    /// <inheritdoc/>
    public override bool IsBroken() => members.Contains(candidate);

    /// <inheritdoc/>
    public override string Message =>
        "A person belongs to an organization at most once: belonging is binary.";
}
