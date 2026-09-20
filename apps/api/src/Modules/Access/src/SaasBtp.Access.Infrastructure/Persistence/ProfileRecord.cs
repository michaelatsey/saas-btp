namespace SaasBtp.Access.Infrastructure.Persistence;

/// <summary>
/// The EF entity for <c>access.profiles</c> — the identity projection. INFRASTRUCTURE by design
/// (Design Package §0): the projection is not a business fact, so it has no Domain type; it exists
/// only here, where it is written inside the founding transaction, first among the writes.
/// </summary>
/// <param name="userId">The authenticated identity (equals <c>auth.users.id</c>).</param>
/// <param name="email">The normalized e-mail (lowercase, trimmed — ADR-ARCH-011).</param>
/// <param name="fullName">The person's full name.</param>
/// <param name="createdAtUtc">The domain-supplied creation instant.</param>
internal sealed class ProfileRecord(Guid userId, string email, string fullName, DateTime createdAtUtc)
{
    /// <summary>The authenticated identity — the primary key.</summary>
    public Guid UserId { get; } = userId;

    /// <summary>The normalized e-mail.</summary>
    public string Email { get; } = email;

    /// <summary>The person's full name.</summary>
    public string FullName { get; } = fullName;

    /// <summary>Optional given name — not collected by BP-001; nullable in the schema.</summary>
    public string? GivenName { get; }

    /// <summary>Optional family name — not collected by BP-001; nullable in the schema.</summary>
    public string? FamilyName { get; }

    /// <summary>Optional job function — not collected by BP-001; nullable in the schema.</summary>
    public string? JobFunction { get; }

    /// <summary>The UTC creation instant, domain-supplied (no database default).</summary>
    public DateTime CreatedAtUtc { get; } = createdAtUtc;
}
