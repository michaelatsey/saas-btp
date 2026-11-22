namespace SaasBtp.Access.Features.Organizations.Found;

/// <summary>
/// The body of <c>POST /api/v1/organizations</c> as the contract states it (contract.md §2): the
/// type the OpenAPI document describes, every property required and never null. It names no
/// founder: the founder is the signed-in person (BP001-R2). Properties it does not define are
/// ignored.
/// </summary>
/// <remarks>The endpoint reads a body as <see cref="ReceivedFoundCompanyRequest"/>, which tolerates what this type forbids, so that each fault gets its answer.</remarks>
/// <param name="OrganizationId">
/// The request's reference, a UUIDv7 the client generates before the first submission, the same for
/// every submission of the request (BP001-R3).
/// </param>
/// <param name="OrganizationName">The company name, at most 200 characters once the white space around it is removed (BP001-R7).</param>
/// <param name="WorkspaceName">The first workspace name, measured as the company name.</param>
internal sealed record FoundCompanyRequest(Guid OrganizationId, string OrganizationName, string WorkspaceName);

/// <summary>
/// A body of <c>POST /api/v1/organizations</c> as it is received: every property may be absent or
/// null, so that an absent or null <c>organizationId</c> is answered 400 and an absent or null name
/// 422, as contract.md §2 states, rather than failing to bind.
/// </summary>
/// <param name="OrganizationId">The reference, when present.</param>
/// <param name="OrganizationName">The company name, as declared.</param>
/// <param name="WorkspaceName">The first workspace name, as declared.</param>
internal sealed record ReceivedFoundCompanyRequest(Guid? OrganizationId, string? OrganizationName, string? WorkspaceName);
