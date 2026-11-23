using SaasBtp.Access.Domain.Organizations;

namespace SaasBtp.Access.Features.Organizations;

/// <summary>
/// An organization, as the API represents it (contract.md §2): the one representation of a founding,
/// a replay and a read. It names no owner and no member (INV-ACCESS-09).
/// </summary>
/// <param name="Id">Its identifier, the reference of the request that founded it.</param>
/// <param name="Name">Its name, as declared once the white space around it is removed.</param>
/// <param name="Status">`declared` or `verified`.</param>
internal sealed record OrganizationResponse(Guid Id, string Name, string Status)
{
    /// <summary>The representation of <paramref name="organization"/>.</summary>
    public static OrganizationResponse Of(Organization organization) =>
        new(organization.Id.Value, organization.Name.Value, StatusToken(organization.Status));

    /// <summary>The wire token of a status (<c>naming.md</c>, Enum value style).</summary>
    private static string StatusToken(OrganizationStatus status) => status switch
    {
        OrganizationStatus.Declared => "declared",
        OrganizationStatus.Verified => "verified",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "An organization status has no token."),
    };
}
