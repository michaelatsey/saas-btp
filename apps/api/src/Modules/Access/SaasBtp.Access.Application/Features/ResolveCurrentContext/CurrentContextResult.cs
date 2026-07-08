namespace SaasBtp.Access.Application.Features.ResolveCurrentContext;

/// <summary>
/// The org-level context returned by <c>GET /me</c>: identity, tenant, and roles.
/// </summary>
/// <remarks>
/// There is no site scope here — site/role membership is deferred by ADR-ARCH-004. Serialized to
/// camelCase on the wire (<c>userId</c>, <c>email</c>, <c>tenantId</c>, <c>roles</c>) by the
/// ASP.NET Core System.Text.Json default (naming.md).
/// </remarks>
/// <param name="UserId">The authenticated user's identifier (JWT <c>sub</c>).</param>
/// <param name="Email">The user's email when present on the token; otherwise <see langword="null"/>.</param>
/// <param name="TenantId">The resolved tenant identifier (from the tenancy pipeline, store-validated).</param>
/// <param name="Roles">The user's org-level role names; empty when none, never <see langword="null"/>.</param>
public sealed record CurrentContextResult(
    Guid UserId,
    string? Email,
    Guid TenantId,
    IReadOnlyList<string> Roles);
