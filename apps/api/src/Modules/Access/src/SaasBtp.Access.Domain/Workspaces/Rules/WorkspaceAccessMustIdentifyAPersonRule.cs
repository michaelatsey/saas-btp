using MicroKit.Domain.Rules;
using SaasBtp.Access.Domain.Identity;

namespace SaasBtp.Access.Domain.Workspaces.Rules;

/// <summary>
/// An access edge is meaningless unless it names somebody: authorization is always a person,
/// somewhere. The "somewhere" half is guaranteed structurally — the edge lives inside the workspace
/// it authorizes, so it cannot point at a workspace that does not exist.
/// </summary>
/// <param name="person">The authorized person's identity under test.</param>
public sealed class WorkspaceAccessMustIdentifyAPersonRule(PersonId person) : BusinessRule
{
    /// <inheritdoc/>
    public override bool IsBroken() => person.IsUnidentified;

    /// <inheritdoc/>
    public override string Message => "Workspace access must identify the person it authorizes.";
}
