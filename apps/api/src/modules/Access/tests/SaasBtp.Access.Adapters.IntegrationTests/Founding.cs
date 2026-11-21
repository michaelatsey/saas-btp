namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The records of one founding (BP-001 TSK-2), under fresh identifiers, inserted as their founder
/// in the order of ADR-ARCH-033 §5, each read back with <c>RETURNING</c>.
/// </summary>
internal sealed record Founding(
    Guid UserId,
    Guid ProfileId,
    Guid OwnershipId,
    Guid OrganizationId,
    Guid MembershipId,
    Guid AccessId,
    Guid WorkspaceId,
    string OrganizationName,
    string WorkspaceName)
{
    /// <summary>A founding of a new person, or of <paramref name="userId"/>'s with a new profile.</summary>
    public static Founding New(Guid? userId = null, string? organizationName = null, string? workspaceName = null)
    {
        var name = organizationName ?? $"Company {Guid.NewGuid():N}";
        return new Founding(
            userId ?? Guid.NewGuid(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            name,
            workspaceName ?? name);
    }

    /// <summary>Founds a new company as a new person, and commits.</summary>
    public static async Task<Founding> CommitAsync(
        AccessDatabase database, string? organizationName = null, string? workspaceName = null)
    {
        var founding = New(organizationName: organizationName, workspaceName: workspaceName);
        await using var session = await database.AsAsync(founding.UserId);
        await founding.InsertAsync(session);
        await session.CommitAsync();
        return founding;
    }

    /// <summary>
    /// Founds a second company as the same person, reusing their profile (BP001-AC-03), and commits.
    /// </summary>
    public async Task<Founding> CommitAnotherAsync(AccessDatabase database)
    {
        var another = New(UserId) with { ProfileId = ProfileId };
        await using var session = await database.AsAsync(UserId);
        await another.InsertAsync(session, withProfile: false);
        await session.CommitAsync();
        return another;
    }

    /// <summary>Profile, ownership, organization, membership, access, workspace, each with <c>RETURNING</c>.</summary>
    public async Task InsertAsync(Session session, bool withProfile = true)
    {
        if (withProfile)
        {
            await session.InsertProfileAsync(ProfileId, UserId);
        }

        await session.InsertOwnershipAsync(OwnershipId, ProfileId, OrganizationId);
        await session.InsertOrganizationAsync(OrganizationId, OrganizationName, OwnershipId);
        await session.InsertMembershipAsync(MembershipId, ProfileId, OrganizationId);
        await session.InsertAccessAsync(AccessId, ProfileId, WorkspaceId, OrganizationId);
        await session.InsertWorkspaceAsync(WorkspaceId, OrganizationId, WorkspaceName);
    }
}
