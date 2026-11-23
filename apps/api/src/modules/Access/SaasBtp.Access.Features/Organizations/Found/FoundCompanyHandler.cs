using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;
using SaasBtp.Access.Features.Ports;

namespace SaasBtp.Access.Features.Organizations.Found;

/// <summary>
/// Founds a company for the caller (BP-001, TSK-1 and TSK-2), in the order of contract.md §2 once
/// the host has authenticated the request: the body (422), then the identity level (403,
/// ADR-ARCH-038 decision 7), then the founding's store. Nothing is written on a refusal.
/// </summary>
/// <remarks>
/// The reference is the idempotency key (BP001-R3, contract.md §3): when the founding's store finds
/// it already in use, the handler reads, in a new transaction, whether the caller is the current
/// owner of the organization it identifies. If so, the request is a replay, answered with that
/// organization, its payload not compared; otherwise the reference is in use. Any other failure
/// propagates.
/// </remarks>
internal sealed class FoundCompanyHandler(ICaller caller, IFoundingStore founding)
{
    /// <summary>Checks the request, then founds the company for the caller.</summary>
    /// <param name="reference">The body's <c>organizationId</c>, a UUID.</param>
    /// <param name="organizationName">The body's company name, as declared.</param>
    /// <param name="workspaceName">The body's first workspace name, as declared.</param>
    /// <param name="cancellationToken">Cancels the founding.</param>
    /// <returns>What the request comes to.</returns>
    /// <exception cref="InvalidOperationException">The request has no authenticated caller.</exception>
    public async Task<FoundCompanyOutcome> HandleAsync(
        Guid reference, string? organizationName, string? workspaceName, CancellationToken cancellationToken = default)
    {
        // Every field at fault, in the order of the body, without its reason (ADR-ARCH-040).
        var faults = new List<string>(3);
        if (!OrganizationId.TryCreate(reference, out var organizationId))
        {
            faults.Add(FoundCompanyField.OrganizationId);
        }

        if (!OrganizationName.TryCreate(organizationName, out var company, out _))
        {
            faults.Add(FoundCompanyField.OrganizationName);
        }

        if (!WorkspaceName.TryCreate(workspaceName, out var workspace, out _))
        {
            faults.Add(FoundCompanyField.WorkspaceName);
        }

        if (faults.Count > 0 || company is null || workspace is null)
        {
            return new FoundCompanyOutcome.Invalid(faults);
        }

        // The fallback policy lets no request without a caller reach the endpoint.
        var current = caller.Current
            ?? throw new InvalidOperationException("A founding reached the handler without an authenticated caller.");

        if (!current.HasConfirmedContact)
        {
            return new FoundCompanyOutcome.NotConfirmed();
        }

        var attempt = await founding.FoundAsync(organizationId, company, workspace, current.Subject, cancellationToken)
            .ConfigureAwait(false);
        switch (attempt)
        {
            case FoundingAttempt.Founded founded:
                return new FoundCompanyOutcome.Founded(founded.Company);

            case FoundingAttempt.ReferenceInUse:
                var owned = await founding.FindOwnedAsync(organizationId, current.Subject, cancellationToken).ConfigureAwait(false);
                return owned is null ? new FoundCompanyOutcome.ReferenceInUse() : new FoundCompanyOutcome.Replayed(owned);

            default:
                throw new InvalidOperationException($"The founding came to an attempt the handler does not answer: {attempt.GetType().Name}.");
        }
    }
}

/// <summary>The fields of the founding's body, as the wire names them (<c>naming.md</c>).</summary>
internal static class FoundCompanyField
{
    public const string OrganizationId = "organizationId";

    public const string OrganizationName = "organizationName";

    public const string WorkspaceName = "workspaceName";
}

/// <summary>What a founding request comes to.</summary>
internal abstract record FoundCompanyOutcome
{
    private FoundCompanyOutcome()
    {
    }

    /// <summary>The company is founded and committed.</summary>
    /// <param name="Company">What the founding created.</param>
    internal sealed record Founded(FoundedCompany Company) : FoundCompanyOutcome;

    /// <summary>The body is refused: 422, nothing written.</summary>
    /// <param name="Fields">The fields at fault, in the order of the body, as <see cref="FoundCompanyField"/> names them.</param>
    internal sealed record Invalid(IReadOnlyList<string> Fields) : FoundCompanyOutcome;

    /// <summary>The caller does not hold the confirmed-contact level (BP001-D1): 403, nothing written.</summary>
    internal sealed record NotConfirmed : FoundCompanyOutcome;

    /// <summary>The reference identifies an organization the caller currently owns: a replay, 200, nothing written (contract.md §3).</summary>
    /// <param name="Organization">The organization the request founded.</param>
    internal sealed record Replayed(Organization Organization) : FoundCompanyOutcome;

    /// <summary>The reference identifies any other organization: 409, nothing written, nothing of it revealed (INV-ACCESS-09).</summary>
    internal sealed record ReferenceInUse : FoundCompanyOutcome;
}
