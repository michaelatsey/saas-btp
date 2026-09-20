using System.Reflection;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Persistence.Abstractions;
using MicroKit.Result;
using NSubstitute;
using SaasBtp.Access.Application.Features.FoundCompanySpace;
using SaasBtp.Access.Domain.Identity;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;
using Shouldly;
using Xunit;

namespace SaasBtp.Access.Application.UnitTests;

/// <summary>
/// Orchestration contract of the founding use case. These are the guarantees the Design Package
/// places on the handler specifically — identity outside the transaction, one atomic unit, one
/// instant, and the founder's identity taken from the auth boundary rather than the request body.
/// </summary>
/// <remarks>
/// The contract is unchanged by ADR-ARCH-016; what changed is who guarantees what. The three axes are
/// no longer six sequential repository calls the handler could get wrong — they are established
/// inside the two founding factories, so these tests assert the handler PASSES THE SAME PERSON to
/// both aggregates and commits once, and let the Domain tests assert the invariants themselves.
/// </remarks>
public sealed class FoundCompanySpaceCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 10, 30, 0, TimeSpan.Zero);

    private readonly IIdentityProvisioner _identity = Substitute.For<IIdentityProvisioner>();
    private readonly IProfileProjection _projection = Substitute.For<IProfileProjection>();
    private readonly IOrganizationRepository _organizations = Substitute.For<IOrganizationRepository>();
    private readonly IWorkspaceRepository _workspaces = Substitute.For<IWorkspaceRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static readonly FoundCompanySpaceCommand Intent = new(
        " Directeur@BTP-Abidjan.CI ", " Kouassi Yao ", " Bâtiment Abidjan ", " Agence Cocody ");

    private FoundCompanySpaceCommandHandler CreateHandler() => new(
        _identity, _projection, _organizations, _workspaces, _unitOfWork,
        new FixedTimeProvider(Now));

    private void GivenIdentityIsEstablished(Guid userId) =>
        _identity.EstablishAsync(Arg.Any<Email>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ProvisionedIdentity(userId, Adopted: false)));

    [Fact]
    public async Task An_identity_failure_writes_no_business_data_at_all()
    {
        _identity.EstablishAsync(Arg.Any<Email>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProvisionedIdentity>(
                new FoundCompanySpaceErrors.IdentityNotEstablished("GoTrue unreachable")));

        var result = await CreateHandler().Handle(Intent);

        result.IsFailure.ShouldBeTrue();
        _projection.DidNotReceiveWithAnyArgs().Add(default!);
        _organizations.DidNotReceiveWithAnyArgs().Add(default!);
        _workspaces.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task An_ambiguous_identity_aborts_before_any_write()
    {
        // ADR-ARCH-011, security-critical: more than one account matching the founder's e-mail must
        // abort loudly — adopting either would attach the founding facts to the wrong person.
        _identity.EstablishAsync(Arg.Any<Email>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProvisionedIdentity>(
                new FoundCompanySpaceErrors.AmbiguousFounderIdentity()));

        var result = await CreateHandler().Handle(Intent);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<FoundCompanySpaceErrors.AmbiguousFounderIdentity>();
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task The_identity_is_established_before_anything_is_enrolled_and_committed_once()
    {
        GivenIdentityIsEstablished(Guid.CreateVersion7());

        await CreateHandler().Handle(Intent);

        // The staging order is the integrity order of Implementation Design §3.3: the projection the
        // relations attach the person through, then the organization, then the workspace that
        // references it.
        Received.InOrder(() =>
        {
            _identity.EstablishAsync(Arg.Any<Email>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            _projection.Add(Arg.Any<FounderProfile>());
            _organizations.Add(Arg.Any<Organization>());
            _workspaces.Add(Arg.Any<Workspace>());
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });

        // Exactly ONE commit: the whole founding unit is one transaction, never several.
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_founder_identity_comes_from_the_auth_boundary_never_from_the_body()
    {
        var establishedId = Guid.CreateVersion7();
        GivenIdentityIsEstablished(establishedId);

        var result = await CreateHandler().Handle(Intent);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FounderUserId.ShouldBe(establishedId);
        _projection.Received(1).Add(Arg.Is<FounderProfile>(profile => profile.UserId == establishedId));
    }

    [Fact]
    public async Task The_email_reaching_the_auth_boundary_is_the_one_that_gets_stored()
    {
        // The lookup address and the stored address must be the same normalized value, or the
        // anti-escalation guarantees of ADR-ARCH-011 stop holding.
        GivenIdentityIsEstablished(Guid.CreateVersion7());

        await CreateHandler().Handle(Intent);

        await _identity.Received(1).EstablishAsync(
            Arg.Is<Email>(email => email.Value == "directeur@btp-abidjan.ci"),
            "Kouassi Yao",
            Arg.Any<CancellationToken>());
        _projection.Received(1).Add(
            Arg.Is<FounderProfile>(profile => profile.Email.Value == "directeur@btp-abidjan.ci"));
    }

    [Fact]
    public async Task One_utc_instant_stamps_the_whole_founding_unit()
    {
        GivenIdentityIsEstablished(Guid.CreateVersion7());
        var (founded, created) = CaptureAggregates();

        await CreateHandler().Handle(Intent);

        _projection.Received(1).Add(Arg.Is<FounderProfile>(profile =>
            profile.CreatedAtUtc == Now.UtcDateTime && profile.CreatedAtUtc.Kind == DateTimeKind.Utc));
        founded.Value.ShouldNotBeNull().CreatedAtUtc.ShouldBe(Now.UtcDateTime);
        created.Value.ShouldNotBeNull().CreatedAtUtc.ShouldBe(Now.UtcDateTime);
    }

    [Fact]
    public async Task The_founding_establishes_the_three_axes_about_the_same_person()
    {
        // ADR-ARCH-016: the handler no longer writes the relations — it hands the SAME PersonId to
        // both factories, which establish ownership, membership and access by construction. What the
        // handler must still guarantee is that it is one person, not three.
        var establishedId = Guid.CreateVersion7();
        GivenIdentityIsEstablished(establishedId);
        var (founded, created) = CaptureAggregates();

        await CreateHandler().Handle(Intent);

        var founder = new PersonId(establishedId);
        var organization = founded.Value.ShouldNotBeNull();
        var workspace = created.Value.ShouldNotBeNull();

        organization.Ownership.Owner.ShouldBe(founder);
        organization.IsMember(founder).ShouldBeTrue();
        workspace.GrantsAccessTo(founder).ShouldBeTrue();
    }

    [Fact]
    public async Task The_workspace_belongs_to_the_organization_that_was_just_founded()
    {
        GivenIdentityIsEstablished(Guid.CreateVersion7());
        var (founded, created) = CaptureAggregates();

        var result = await CreateHandler().Handle(Intent);

        var organization = founded.Value.ShouldNotBeNull();
        var workspace = created.Value.ShouldNotBeNull();

        workspace.OrganizationId.ShouldBe(organization.Id);
        result.Value.OrganizationId.ShouldBe(organization.Id.Value);
        result.Value.WorkspaceId.ShouldBe(workspace.Id.Value);
    }

    [Fact]
    public async Task A_commit_failure_surfaces_and_is_not_swallowed_into_a_success()
    {
        // Infrastructure faults are exceptions, not Results: no business flow anticipates them, and
        // reporting success after a failed commit would claim facts that do not exist.
        GivenIdentityIsEstablished(Guid.CreateVersion7());
        _unitOfWork.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("unique violation")));

        await Should.ThrowAsync<InvalidOperationException>(() => CreateHandler().Handle(Intent).AsTask());
    }

    [Fact]
    public void The_handler_asks_for_no_port_per_relation()
    {
        // Two aggregate roots, two repository ports (ADR-ARCH-016). Asserted on the handler's own
        // dependency list so a reinstated per-relation port cannot come back through the use case.
        var ports = typeof(FoundCompanySpaceCommandHandler)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        ports.Count(port => port.Name.EndsWith("Repository", StringComparison.Ordinal)).ShouldBe(2);
        ports.ShouldContain(typeof(IOrganizationRepository));
        ports.ShouldContain(typeof(IWorkspaceRepository));
    }

    /// <summary>
    /// Captures the two aggregates as they are staged. They are created INSIDE the handler — the
    /// founding factories mint their own ids — so intercepting the repository call is the only way to
    /// observe what was actually built.
    /// </summary>
    private (Captured<Organization> Organization, Captured<Workspace> Workspace) CaptureAggregates()
    {
        var organization = new Captured<Organization>();
        var workspace = new Captured<Workspace>();

        _organizations.Add(Arg.Do<Organization>(value => organization.Value = value));
        _workspaces.Add(Arg.Do<Workspace>(value => workspace.Value = value));

        return (organization, workspace);
    }

    private sealed class Captured<T>
        where T : class
    {
        public T? Value { get; set; }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
