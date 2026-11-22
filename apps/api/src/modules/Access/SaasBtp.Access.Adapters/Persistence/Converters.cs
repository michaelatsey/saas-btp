using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.WorkspaceAccesses;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Adapters.Persistence;

// The identifiers of the Access domain are record structs over a UUID, stored as uuid.

/// <summary>Stores a <see cref="ProfileId"/> as its UUID.</summary>
internal sealed class ProfileIdConverter() : ValueConverter<ProfileId, Guid>(id => id.Value, value => new ProfileId(value));

/// <summary>Stores a <see cref="UserId"/>, the identity provider's identifier, as its UUID.</summary>
internal sealed class UserIdConverter() : ValueConverter<UserId, Guid>(id => id.Value, value => new UserId(value));

/// <summary>Stores an <see cref="OwnershipId"/> as its UUID.</summary>
internal sealed class OwnershipIdConverter() : ValueConverter<OwnershipId, Guid>(id => id.Value, value => new OwnershipId(value));

/// <summary>Stores an <see cref="OrganizationId"/> as its UUID.</summary>
internal sealed class OrganizationIdConverter() : ValueConverter<OrganizationId, Guid>(id => id.Value, value => new OrganizationId(value));

/// <summary>Stores a <see cref="MembershipId"/> as its UUID.</summary>
internal sealed class MembershipIdConverter() : ValueConverter<MembershipId, Guid>(id => id.Value, value => new MembershipId(value));

/// <summary>Stores a <see cref="WorkspaceId"/> as its UUID.</summary>
internal sealed class WorkspaceIdConverter() : ValueConverter<WorkspaceId, Guid>(id => id.Value, value => new WorkspaceId(value));

/// <summary>Stores a <see cref="WorkspaceAccessId"/> as its UUID.</summary>
internal sealed class WorkspaceAccessIdConverter()
    : ValueConverter<WorkspaceAccessId, Guid>(id => id.Value, value => new WorkspaceAccessId(value));

/// <summary>
/// Stores an <see cref="OrganizationName"/> as the measured name; reads it back through
/// <see cref="OrganizationName.TryCreate"/>, the one way to build one.
/// </summary>
internal sealed class OrganizationNameConverter()
    : ValueConverter<OrganizationName, string>(name => name.Value, value => StoredNames.Organization(value));

/// <summary>
/// Stores a <see cref="WorkspaceName"/> as the measured name; reads it back through
/// <see cref="WorkspaceName.TryCreate"/>, the one way to build one.
/// </summary>
internal sealed class WorkspaceNameConverter()
    : ValueConverter<WorkspaceName, string>(name => name.Value, value => StoredNames.Workspace(value));

/// <summary>
/// Stores an <see cref="OrganizationStatus"/> as the token the table's check admits, <c>declared</c>
/// or <c>verified</c> (<c>naming.md</c>, Enum value style).
/// </summary>
internal sealed class OrganizationStatusConverter()
    : ValueConverter<OrganizationStatus, string>(status => StoredStatus.ToToken(status), token => StoredStatus.FromToken(token));

/// <summary>Reads back the names the API wrote, measured, as their domain types.</summary>
internal static class StoredNames
{
    public static OrganizationName Organization(string stored) =>
        OrganizationName.TryCreate(stored, out var name, out var defect)
            ? name
            : throw new InvalidOperationException($"A stored organization name is not valid ({defect}): correct the row.");

    public static WorkspaceName Workspace(string stored) =>
        WorkspaceName.TryCreate(stored, out var name, out var defect)
            ? name
            : throw new InvalidOperationException($"A stored workspace name is not valid ({defect}): correct the row.");
}

/// <summary>The tokens of <see cref="OrganizationStatus"/>.</summary>
internal static class StoredStatus
{
    public static string ToToken(OrganizationStatus status) => status switch
    {
        OrganizationStatus.Declared => "declared",
        OrganizationStatus.Verified => "verified",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "An organization status has no token."),
    };

    public static OrganizationStatus FromToken(string token) => token switch
    {
        "declared" => OrganizationStatus.Declared,
        "verified" => OrganizationStatus.Verified,
        _ => throw new InvalidOperationException($"A stored organization status, '{token}', is not a token of OrganizationStatus."),
    };
}
