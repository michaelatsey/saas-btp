using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.WorkspaceAccesses;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Domain.Founding;

/// <summary>
/// What one founding creates (TSK-2): the founder's profile when they had none, the organization,
/// its one first workspace, and the founder's ownership, membership and access to that workspace.
/// </summary>
public sealed class FoundedCompany
{
    internal FoundedCompany(
        Profile profile,
        bool isProfileCreated,
        Organization organization,
        Workspace workspace,
        Ownership ownership,
        Membership membership,
        WorkspaceAccess access)
    {
        Profile = profile;
        IsProfileCreated = isProfileCreated;
        Organization = organization;
        Workspace = workspace;
        Ownership = ownership;
        Membership = membership;
        Access = access;
    }

    /// <summary>The founder's profile, created by this founding or the one they had.</summary>
    public Profile Profile { get; }

    /// <summary>Whether this founding created the profile (INV-ACCESS-01): the founder had none.</summary>
    public bool IsProfileCreated { get; }

    /// <summary>The organization, Declared, under the request's reference.</summary>
    public Organization Organization { get; }

    /// <summary>The organization's first workspace.</summary>
    public Workspace Workspace { get; }

    /// <summary>The founder's ownership of the organization.</summary>
    public Ownership Ownership { get; }

    /// <summary>The founder's membership of the organization.</summary>
    public Membership Membership { get; }

    /// <summary>The founder's access to the first workspace, and to no other (BP001-R4).</summary>
    public WorkspaceAccess Access { get; }
}
