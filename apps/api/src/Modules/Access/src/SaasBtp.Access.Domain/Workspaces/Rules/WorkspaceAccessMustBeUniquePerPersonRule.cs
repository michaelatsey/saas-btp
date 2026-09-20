using MicroKit.Domain.Rules;
using SaasBtp.Access.Domain.Identity;

namespace SaasBtp.Access.Domain.Workspaces.Rules;

/// <summary>
/// "Can act here" is BINARY: a person holds one access edge on a workspace, or none. A second edge
/// would say nothing the first does not, so granting one to somebody already authorized is refused.
/// </summary>
/// <remarks>
/// This rule is why the access edges live inside the <see cref="Workspace"/> aggregate: deciding it
/// requires seeing the workspace AND all its edges at once. Scattered outside, the model could only
/// delegate the rule to a database unique index — leaving the domain unable to state its own
/// authorization invariant.
/// </remarks>
/// <param name="authorized">The people already holding an edge on the workspace.</param>
/// <param name="candidate">The person about to be granted access.</param>
public sealed class WorkspaceAccessMustBeUniquePerPersonRule(
    IEnumerable<PersonId> authorized, PersonId candidate) : BusinessRule
{
    /// <inheritdoc/>
    public override bool IsBroken() => authorized.Contains(candidate);

    /// <inheritdoc/>
    public override string Message =>
        "A person holds at most one access on a workspace: authorization is binary.";
}
