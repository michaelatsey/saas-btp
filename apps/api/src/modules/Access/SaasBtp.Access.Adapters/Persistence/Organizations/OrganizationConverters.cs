using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SaasBtp.Access.Domain.Organizations;

namespace SaasBtp.Access.Adapters.Persistence.Organizations;

/// <summary>Stores an <see cref="OrganizationId"/> as its UUID.</summary>
internal sealed class OrganizationIdConverter() : ValueConverter<OrganizationId, Guid>(id => id.Value, value => new OrganizationId(value));

/// <summary>
/// Stores an <see cref="OrganizationName"/> as the measured name; reads it back through
/// <see cref="OrganizationName.TryCreate"/>, the one way to build one.
/// </summary>
internal sealed class OrganizationNameConverter()
    : ValueConverter<OrganizationName, string>(name => name.Value, value => StoredOrganizationName.Read(value));

/// <summary>
/// Stores an <see cref="OrganizationStatus"/> as the token the table's check admits, <c>declared</c>
/// or <c>verified</c> (<c>naming.md</c>, Enum value style).
/// </summary>
internal sealed class OrganizationStatusConverter()
    : ValueConverter<OrganizationStatus, string>(status => StoredStatus.ToToken(status), token => StoredStatus.FromToken(token));

/// <summary>Reads back an organization name the API wrote, measured.</summary>
internal static class StoredOrganizationName
{
    public static OrganizationName Read(string stored) =>
        OrganizationName.TryCreate(stored, out var name, out var defect)
            ? name
            : throw new InvalidOperationException($"A stored organization name is not valid ({defect}): correct the row.");
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
