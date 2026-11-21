using MicroKit.Domain.ValueObjects.Common;
using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.WorkspaceAccesses;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Domain.Founding;

/// <summary>
/// The founding of a company (BP-001, TSK-2), with no infrastructure. Persisting what it creates
/// in one unit of work, and the replay of an already used reference (contract.md §3), are not
/// the domain's.
/// </summary>
public static class CompanyFounding
{
    /// <summary>
    /// Founds a company: the founder's profile if they have none (INV-ACCESS-01); the organization,
    /// Declared, under <paramref name="reference"/> (INV-ACCESS-08); its first workspace
    /// (INV-ACCESS-03); the founder's ownership, membership and access to that workspace and to no
    /// other (BP001-R4), each created from the founding's own inputs, none from another relation
    /// (INV-ACCESS-05). Every other identifier is a UUIDv7 and every creation instant the one
    /// instant read from <paramref name="clock"/>; the founder is the acting person (BP001-R2).
    /// </summary>
    /// <param name="reference">
    /// The request's reference, the organization's identifier: a UUIDv7, refused otherwise. The
    /// caller builds it with <see cref="OrganizationId.TryCreate"/>, which answers without an exception.
    /// </param>
    /// <param name="organizationName">The company name, measured.</param>
    /// <param name="workspaceName">The first workspace name, measured.</param>
    /// <param name="founder">The signed-in person who submits the request (BP001-R2).</param>
    /// <param name="founderProfile">The founder's profile, or null when they have none.</param>
    /// <param name="clock">The injected clock.</param>
    /// <returns>What the founding creates.</returns>
    /// <exception cref="ArgumentException">
    /// A reference that is not a UUIDv7 (<see cref="OrganizationId.IsVersion7"/>), a default founder,
    /// or a profile that is not the founder's.
    /// </exception>
    public static FoundedCompany Found(
        OrganizationId reference,
        OrganizationName organizationName,
        WorkspaceName workspaceName,
        UserId founder,
        Profile? founderProfile,
        TimeProvider clock)
    {
        if (!OrganizationId.IsVersion7(reference.Value))
            throw new ArgumentException(
                "The reference must be a UUIDv7 (naming.md, \"Aggregate identifiers\"); build it with OrganizationId.TryCreate.",
                nameof(reference));
        ArgumentNullException.ThrowIfNull(organizationName);
        ArgumentNullException.ThrowIfNull(workspaceName);
        Identifier.Require(founder.Value, nameof(founder));
        ArgumentNullException.ThrowIfNull(clock);
        if (founderProfile is not null && founderProfile.UserId != founder)
            throw new ArgumentException(
                "The profile is not the founder's: the founder is the signed-in person (BP001-R2).", nameof(founderProfile));

        var at = clock.GetUtcNow();
        var by = new Actor(founder.Value.ToString());

        var profile = founderProfile ?? new Profile(ProfileId.New(at), founder, at, by);
        var ownership = new Ownership(OwnershipId.New(at), profile.Id, reference, at, by);
        var workspace = new Workspace(WorkspaceId.New(at), reference, workspaceName, at, by);
        var organization = Organization.Found(reference, organizationName, ownership.Id, workspace.Id, profile.Id, at, by);
        var membership = new Membership(MembershipId.New(at), profile.Id, reference, at, by);
        var access = new WorkspaceAccess(WorkspaceAccessId.New(at), profile.Id, workspace.Id, workspace.OrganizationId, at, by);

        return new FoundedCompany(
            profile, isProfileCreated: founderProfile is null, organization, workspace, ownership, membership, access);
    }
}
