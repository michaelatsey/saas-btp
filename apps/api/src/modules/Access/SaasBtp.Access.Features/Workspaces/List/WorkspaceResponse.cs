namespace SaasBtp.Access.Features.Workspaces.List;

/// <summary>A workspace the caller can enter, as <c>GET /api/v1/workspaces</c> lists it (contract.md §2).</summary>
/// <param name="Id">Its identifier.</param>
/// <param name="Name">Its name.</param>
/// <param name="OrganizationId">The identifier of its organization.</param>
internal sealed record WorkspaceResponse(Guid Id, string Name, Guid OrganizationId);
