namespace SaasBtp.Access.Domain.Organizations;

/// <summary>The status of an organization (docs/domain/access.md, INV-ACCESS-08).</summary>
public enum OrganizationStatus
{
    /// <summary>Taken as declared by its founder; nobody's claim to represent it was checked (ADR-PROD-002).</summary>
    Declared = 0,

    /// <summary>Verified, which only verification (BP-009) makes it.</summary>
    Verified = 1,
}
