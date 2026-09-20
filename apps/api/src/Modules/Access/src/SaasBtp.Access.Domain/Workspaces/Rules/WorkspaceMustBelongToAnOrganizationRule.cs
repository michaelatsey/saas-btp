using MicroKit.Domain.Rules;
using SaasBtp.Access.Domain.Organizations;

namespace SaasBtp.Access.Domain.Workspaces.Rules;

/// <summary>
/// A workspace never exists outside an organization: it is the organization's autonomous
/// operational space, so the organization it belongs to must be identified at creation.
/// </summary>
/// <param name="organizationId">The owning organization's identity under test.</param>
public sealed class WorkspaceMustBelongToAnOrganizationRule(OrganizationId organizationId) : BusinessRule
{
    /// <inheritdoc/>
    public override bool IsBroken() => organizationId.Value == Guid.Empty;

    /// <inheritdoc/>
    public override string Message => "A workspace must belong to an identified organization.";
}
