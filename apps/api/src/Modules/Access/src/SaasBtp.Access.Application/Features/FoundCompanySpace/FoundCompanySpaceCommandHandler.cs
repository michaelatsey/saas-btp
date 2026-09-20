using MicroKit.Domain.ValueObjects.Common;
using MicroKit.MediatR.Handlers;
using MicroKit.Persistence.Abstractions;
using MicroKit.Result;
using SaasBtp.Access.Domain.Identity;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Application.Features.FoundCompanySpace;

/// <summary>
/// The founding use case (BP-001): the only participant that sees the whole process. It coordinates
/// tasks and delegates work to the domain — it holds no business rule (those live in the aggregates)
/// and no input validation (that lives in the validator).
/// </summary>
/// <remarks>
/// Order is load-bearing:
/// <list type="number">
/// <item><description>establish the identity at the auth boundary, OUTSIDE any transaction — a
/// failure here is terminal, with no business state;</description></item>
/// <item><description>stamp ONE UTC founding instant for the whole unit;</description></item>
/// <item><description>enrol the projection and the two aggregates, then commit ONCE — every write
/// lands in a single transaction, or none does.</description></item>
/// </list>
/// A commit failure rolls the whole unit back and may leave an orphaned auth identity: accepted by
/// design (an orphan identity is harmless and is deterministically adopted on retry; an orphan
/// workspace is forbidden by INV-4).
/// <para>
/// THREE writes, not six (ADR-ARCH-016). Ownership, membership and the workspace access edge are
/// established BY CONSTRUCTION inside <see cref="Organization.Found"/> and
/// <see cref="Workspace.Found"/>, so this handler cannot forget one and cannot order them wrongly —
/// the aggregates guarantee what six sequential calls previously only convened. The three axes stay
/// semantically independent: nothing here reads one to produce another; the founder is passed to each
/// factory as the SAME <see cref="PersonId"/>, three times, deliberately.
/// </para>
/// <para>
/// The commit boundary is MicroKit's <see cref="IUnitOfWork"/>, injected here because MicroKit
/// prescribes exactly that — "inject IUnitOfWork in command handlers only; call CommitAsync exactly
/// once per command handler invocation, after all staging operations". Its EF implementation is one
/// <c>SaveChangesAsync</c>, so the writes are one database transaction with no hand-rolled
/// transaction code. The repository CONTRACTS stay in the Domain (they are collections of
/// aggregates); only the COMMIT is a persistence concern (MicroKit ADR-001).
/// </para>
/// <para>
/// The commit stays HERE and must not be removed in favour of MicroKit's <c>TransactionBehavior</c>
/// (order 700) until that behavior can actually be registered AND actually commits. As shipped in
/// <c>MicroKit.MediatR.Behaviors 1.0.0-preview.2</c> it does neither: its constructor is
/// <c>(ITransactionalContext, IDomainEventsDispatcher)</c> — no <c>IUnitOfWork</c>, no
/// <c>SaveChangesAsync</c> on its path — and <c>IDomainEventsDispatcher</c> cannot be resolved at all
/// (see <c>AccessPipelineTests</c>). Deleting this line today would leave the founding transaction
/// writing nothing, silently.
/// </para>
/// <para>
/// The handler emits no log line and raises no event. Observability is a pipeline concern in MicroKit
/// (LoggingBehavior, order 100), and BP-001's design contract assigns no domain or integration event,
/// so there is nothing for an outbox to carry.
/// </para>
/// <para>
/// RULE-4 (a repeated founding intent must not produce several workspaces) has NO guarantor here:
/// it is an open gap in the design contract, and inventing one would be a decision this handler is
/// not entitled to take.
/// </para>
/// </remarks>
/// <param name="identityProvisioner">The auth boundary that establishes, or adopts, the founder.</param>
/// <param name="profileProjection">The identity projection written inside the founding unit.</param>
/// <param name="organizations">The collection of organizations.</param>
/// <param name="workspaces">The collection of workspaces.</param>
/// <param name="unitOfWork">The commit boundary of the founding unit.</param>
/// <param name="timeProvider">The source of the single UTC founding instant.</param>
public sealed class FoundCompanySpaceCommandHandler(
    IIdentityProvisioner identityProvisioner,
    IProfileProjection profileProjection,
    IOrganizationRepository organizations,
    IWorkspaceRepository workspaces,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<FoundCompanySpaceCommand, Result<FoundCompanySpaceResponse>>
{
    /// <summary>Executes the founding capability.</summary>
    /// <param name="command">The founding intent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success — the minimal context the founder needs to continue; failure — the identity rupture,
    /// with no business data written.
    /// </returns>
    public async ValueTask<Result<FoundCompanySpaceResponse>> Handle(
        FoundCompanySpaceCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Normalization happens ONCE, in the value object, and the SAME instance flows to the
        // identity boundary and into the projection — so the address that is looked up is by
        // construction the address that is stored (ADR-ARCH-011).
        var email = new Email(command.FounderEmail);
        var fullName = command.FounderFullName.Trim();

        // (1) Identity — outside the transaction. The founder's id comes from the boundary's
        // RESULT, never from the request body.
        var identity = await identityProvisioner
            .EstablishAsync(email, fullName, ct)
            .ConfigureAwait(false);
        if (identity.IsFailure)
            return Result.Failure<FoundCompanySpaceResponse>(identity.Error);

        // The Guid crosses into the domain's vocabulary here and nowhere else: past this line the
        // founder is a PersonId, which is what the aggregates accept.
        var founder = new PersonId(identity.Value.UserId);

        // (2) One instant for the whole unit — supplied to the domain, never read from an ambient
        // clock inside it, never defaulted by the database. UtcDateTime carries Kind = Utc, which is
        // the precondition both founding factories assert.
        var foundedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Each factory establishes its own facts: the organization is founded WITH its owner and its
        // first member, the workspace WITH the founder's access edge.
        var organization = Organization.Found(
            command.OrganizationDisplayName.Trim(), founder, foundedAtUtc);
        var workspace = Workspace.Found(
            organization.Id, command.WorkspaceDisplayName.Trim(), founder, foundedAtUtc);

        // (3) The atomic unit, staged in the integrity order the design states (§3.3): the
        // projection the relations attach the person through, then the organization, then the
        // workspace that references it.
        profileProjection.Add(new FounderProfile(founder.Value, email, fullName, foundedAtUtc));
        organizations.Add(organization);
        workspaces.Add(workspace);

        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        return new FoundCompanySpaceResponse(organization.Id.Value, workspace.Id.Value, founder.Value);
    }
}
