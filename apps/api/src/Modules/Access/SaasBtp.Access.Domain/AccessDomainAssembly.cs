namespace SaasBtp.Access.Domain;

/// <summary>
/// Assembly anchor for the Access domain layer.
/// </summary>
/// <remarks>
/// Story #4 is claims-only and, per ADR-ARCH-004, the Access module owns no membership domain
/// model (no entity, aggregate, repository, service, or schema). This type carries no behavior;
/// it exists solely so the architecture tests can reference the <c>SaasBtp.Access.Domain</c>
/// assembly. It will be removed once a real Access domain model exists.
/// </remarks>
public sealed class AccessDomainAssembly
{
    private AccessDomainAssembly()
    {
    }
}
