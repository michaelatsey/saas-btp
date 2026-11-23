using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Features.Ports;

namespace SaasBtp.Access.Features.Organizations.Read;

/// <summary>
/// Reads an organization for the caller (contract.md §2): the organization when the select policies
/// let the caller see it, and nothing otherwise, whether or not it exists (ADR-ARCH-032 §5). The
/// handler decides no access of its own.
/// </summary>
internal sealed class ReadOrganizationHandler(IAccessQueries queries)
{
    /// <summary>The organization <paramref name="id"/> identifies, when the caller may see it.</summary>
    /// <param name="id">The route's identifier, a UUID.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The organization, or <see langword="null"/>. A UUID that is not version 7 identifies no
    /// organization (<c>naming.md</c>, Aggregate identifiers): it is answered as one that exists
    /// nowhere, without a read.
    /// </returns>
    public async Task<Organization?> HandleAsync(Guid id, CancellationToken cancellationToken = default) =>
        OrganizationId.TryCreate(id, out var organizationId)
            ? await queries.FindVisibleOrganizationAsync(organizationId, cancellationToken).ConfigureAwait(false)
            : null;
}
