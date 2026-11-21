using MicroKit.Domain.Identifiers;

namespace SaasBtp.Access.Domain.Organizations;

/// <summary>
/// The identifier of an organization: the reference the client gives the company to found before
/// the first submission of its founding request, a UUIDv7 (naming.md, "Aggregate identifiers";
/// BP001-R3).
/// </summary>
/// <param name="Value">The UUID.</param>
public readonly record struct OrganizationId(Guid Value) : IEntityId
{
    /// <summary>A new identifier: a UUIDv7 whose timestamp is <paramref name="at"/>.</summary>
    /// <param name="at">The instant of the creation.</param>
    public static OrganizationId New(DateTimeOffset at) => new(Guid.CreateVersion7(at));

    /// <summary>
    /// Takes <paramref name="reference"/> as an organization's identifier when it is a UUIDv7 of
    /// RFC 9562: version 7 and the RFC 9562 variant (contract.md §2 refuses any other).
    /// </summary>
    /// <param name="reference">The reference the client sent.</param>
    /// <param name="id">The identifier, or <see langword="default"/> when refused.</param>
    /// <returns>Whether <paramref name="reference"/> is a UUIDv7.</returns>
    public static bool TryCreate(Guid reference, out OrganizationId id)
    {
        id = IsVersion7(reference) ? new OrganizationId(reference) : default;
        return id != default;
    }

    /// <summary>Whether <paramref name="value"/> is a UUIDv7 of RFC 9562: version 7, variant <c>10xx</c>.</summary>
    /// <param name="value">The UUID to check.</param>
    /// <returns><see langword="true"/> for a UUIDv7.</returns>
    public static bool IsVersion7(Guid value) => value.Version == 7 && (value.Variant & 0b1100) == 0b1000;
}
