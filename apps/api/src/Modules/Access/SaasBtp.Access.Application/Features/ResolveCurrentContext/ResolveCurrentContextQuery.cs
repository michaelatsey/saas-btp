namespace SaasBtp.Access.Application.Features.ResolveCurrentContext;

/// <summary>
/// Query to resolve the current caller's org-level context (identity, tenant, roles).
/// </summary>
/// <remarks>
/// Carries no client input: the "input" is the ambient security and tenant context established by
/// the request pipeline (the authenticated principal and the store-validated tenant). A validator
/// is intentionally omitted for this slice; scope-presence is enforced by the handler as typed
/// errors. The slice template's validator step remains mandatory for future input-carrying slices.
/// </remarks>
public sealed record ResolveCurrentContextQuery;
