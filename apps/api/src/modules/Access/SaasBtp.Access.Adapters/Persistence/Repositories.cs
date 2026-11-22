using Microsoft.EntityFrameworkCore;
using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.WorkspaceAccesses;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Adapters.Persistence;

// The repository ports of the Access domain, on the scoped context. An addition is tracked and
// written by the unit of work's next commit: a repository never saves (analyzer MKP003), and every
// command runs in the unit of work's transaction (ADR-ARCH-033 §4).

/// <summary>The profiles, on the Access context.</summary>
internal sealed class ProfileRepository(AccessDbContext context) : IProfileRepository
{
    public async ValueTask<Profile?> FindByUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
        await context.Set<Profile>().SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken).ConfigureAwait(false);

    public ValueTask AddAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        context.Add(profile);
        return ValueTask.CompletedTask;
    }
}

/// <summary>The ownerships, on the Access context.</summary>
internal sealed class OwnershipRepository(AccessDbContext context) : IOwnershipRepository
{
    public ValueTask AddAsync(Ownership ownership, CancellationToken cancellationToken = default)
    {
        context.Add(ownership);
        return ValueTask.CompletedTask;
    }
}

/// <summary>The organizations, on the Access context.</summary>
internal sealed class OrganizationRepository(AccessDbContext context) : IOrganizationRepository
{
    public ValueTask AddAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        context.Add(organization);
        return ValueTask.CompletedTask;
    }
}

/// <summary>The memberships, on the Access context.</summary>
internal sealed class MembershipRepository(AccessDbContext context) : IMembershipRepository
{
    public ValueTask AddAsync(Membership membership, CancellationToken cancellationToken = default)
    {
        context.Add(membership);
        return ValueTask.CompletedTask;
    }
}

/// <summary>The workspace accesses, on the Access context.</summary>
internal sealed class WorkspaceAccessRepository(AccessDbContext context) : IWorkspaceAccessRepository
{
    public ValueTask AddAsync(WorkspaceAccess access, CancellationToken cancellationToken = default)
    {
        context.Add(access);
        return ValueTask.CompletedTask;
    }
}

/// <summary>The workspaces, on the Access context.</summary>
internal sealed class WorkspaceRepository(AccessDbContext context) : IWorkspaceRepository
{
    public ValueTask AddAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        context.Add(workspace);
        return ValueTask.CompletedTask;
    }
}
