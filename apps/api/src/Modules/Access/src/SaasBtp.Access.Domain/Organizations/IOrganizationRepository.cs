namespace SaasBtp.Access.Domain.Organizations;

/// <summary>
/// The collection of <see cref="Organization"/> aggregates. A domain-owned contract: the model
/// states how its aggregates are stored and retrieved, and an Infrastructure adapter implements it.
/// </summary>
/// <remarks>
/// One repository per aggregate root, and the Access domain has exactly two (ADR-ARCH-016). The
/// ownership and membership facts have no repository of their own: they are components of the
/// organization, reached and persisted through it.
/// <para>
/// Deliberately narrow — <c>Add</c> only. BP-001 is the only writer today and it only founds
/// organizations; exposing Update/Remove/Find before a use case needs them would be speculative
/// surface on a contract the whole module depends on. Each new use case widens it explicitly.
/// </para>
/// <para>
/// <c>Add</c> is synchronous on purpose: enrolling an aggregate in the unit of work performs no I/O.
/// The I/O is the commit, which the command handler triggers through MicroKit's
/// <c>IUnitOfWork</c> — a persistence concern the Domain deliberately does not name
/// (MicroKit ADR-001).
/// </para>
/// </remarks>
public interface IOrganizationRepository
{
    /// <summary>Enrols a newly founded organization in the current unit of work.</summary>
    /// <param name="organization">The organization to persist.</param>
    void Add(Organization organization);
}
