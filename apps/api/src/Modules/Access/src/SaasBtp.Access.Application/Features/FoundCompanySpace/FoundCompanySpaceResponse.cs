namespace SaasBtp.Access.Application.Features.FoundCompanySpace;

/// <summary>
/// The founding terminal state, as the minimal context the founder needs to continue
/// (Implementation Design §4.3): the organization is usable by its founder.
/// </summary>
/// <param name="OrganizationId">The declared organization's identity.</param>
/// <param name="WorkspaceId">The first workspace's identity.</param>
/// <param name="FounderUserId">The founder's established authenticated identity.</param>
public sealed record FoundCompanySpaceResponse(
    Guid OrganizationId,
    Guid WorkspaceId,
    Guid FounderUserId);
