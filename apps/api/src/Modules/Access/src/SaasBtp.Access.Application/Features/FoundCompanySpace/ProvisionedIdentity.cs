namespace SaasBtp.Access.Application.Features.FoundCompanySpace;

/// <summary>
/// The identity boundary's result: a recognized authenticated identity the founding facts can
/// attach to.
/// </summary>
/// <param name="UserId">The <c>auth.users</c> identity (created, or adopted from an orphan).</param>
/// <param name="Adopted">
/// True when an existing orphaned account was adopted instead of created (ADR-ARCH-011 — a previous
/// attempt died between createUser and the business transaction; the orphan is harmless and is
/// deterministically re-used, never compensated).
/// </param>
public sealed record ProvisionedIdentity(Guid UserId, bool Adopted);
