using MicroKit.MediatR.Requests;
using MicroKit.Result;

namespace SaasBtp.Access.Application.Features.FoundCompanySpace;

/// <summary>
/// The founding intent (BP-001): a director external to the platform requests the creation of their
/// company space. One process capability, not a CRUD — it produces atomically the identity
/// projection plus the five business facts (Implementation Design §4.1).
/// </summary>
/// <remarks>
/// The founder's identity for the authorization facts derives from the IDENTITY BOUNDARY's result
/// (createUser / orphan adoption), never from this body (Implementation Design §4.4): the command
/// carries what is needed to ESTABLISH the identity, not the identity itself.
/// </remarks>
/// <param name="FounderEmail">The founder's e-mail — the element the identity is established from.</param>
/// <param name="FounderFullName">The founder's full name, projected into <c>access.profiles</c>.</param>
/// <param name="OrganizationDisplayName">The declared organization's display name.</param>
/// <param name="WorkspaceDisplayName">The first workspace's display name.</param>
public sealed record FoundCompanySpaceCommand(
    string FounderEmail,
    string FounderFullName,
    string OrganizationDisplayName,
    string WorkspaceDisplayName) : ICommand<Result<FoundCompanySpaceResponse>>;
