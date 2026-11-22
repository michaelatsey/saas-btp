namespace SaasBtp.Access.Features.Organizations.Found;

/// <summary>An organization, as the API represents it (contract.md §2).</summary>
/// <param name="Id">Its identifier, the reference of the request that founded it.</param>
/// <param name="Name">Its name, as declared once the white space around it is removed.</param>
/// <param name="Status">`declared` or `verified`.</param>
internal sealed record OrganizationResponse(Guid Id, string Name, string Status);
