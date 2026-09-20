using System.Reflection;
using MicroKit.Domain.Aggregates;
using SaasBtp.Access.Domain.Identity;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;
using Shouldly;
using Xunit;

namespace SaasBtp.Access.Domain.UnitTests;

/// <summary>
/// The shape of the model itself: the Access domain has exactly TWO consistency boundaries, and the
/// three relations live inside the boundary that owns their invariant (ADR-ARCH-016). These tests
/// exist because the previous model had five roots — three of them relations with no invariant, no
/// lifecycle and nothing to enclose — and nothing in the code stopped that from coming back.
/// </summary>
public sealed class AggregateBoundaryTests
{
    private static readonly DateTime FoundingInstant = new(2026, 8, 16, 10, 30, 0, DateTimeKind.Utc);

    private static Assembly DomainAssembly => typeof(Organization).Assembly;

    private static Type[] AggregateRoots() =>
        [.. DomainAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.BaseType is { IsGenericType: true }
                && type.BaseType.GetGenericTypeDefinition() == typeof(AggregateRoot<>))];

    [Fact]
    public void There_are_exactly_two_aggregate_roots()
        => AggregateRoots().ShouldBe([typeof(Organization), typeof(Workspace)], ignoreOrder: true);

    [Fact]
    public void A_relation_that_encloses_no_invariant_is_not_a_root()
    {
        // Ownership, membership and the access edge each participate in an invariant that spans an
        // organization or a workspace AND the relation. An invariant spanning several objects
        // defines the boundary containing them — so the relations are inside, not beside.
        var roots = AggregateRoots();

        roots.ShouldNotContain(typeof(Ownership));
        roots.ShouldNotContain(typeof(OrganizationMembership));
        roots.ShouldNotContain(typeof(WorkspaceAccess));
    }

    [Fact]
    public void There_is_exactly_one_repository_contract_per_aggregate_root()
    {
        // Two roots, two repositories. The relations get none: they are reached and persisted
        // through the root that contains them.
        var repositories = DomainAssembly.GetTypes()
            .Where(type => type.IsInterface)
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToArray();

        repositories.ShouldBe(["IOrganizationRepository", "IWorkspaceRepository"], ignoreOrder: true);
    }

    [Fact]
    public void Only_the_access_edge_keeps_an_identity_of_its_own()
    {
        // The asymmetry is the model speaking: the access edge must survive its own revocation
        // (Implementation Design §3.5); ownership and membership are facts defined by their values,
        // and no process revokes them today.
        typeof(WorkspaceAccess).GetProperty("Id").ShouldNotBeNull();
        typeof(Ownership).GetProperty("Id").ShouldBeNull();
        typeof(OrganizationMembership).GetProperty("Id").ShouldBeNull();
    }

    [Fact]
    public void The_relations_cannot_be_created_outside_the_aggregate_that_owns_them()
    {
        // No public constructor and no public factory: the root is the only entry point, which is
        // what makes its guarantee a guarantee rather than a convention.
        foreach (var relation in (Type[])[typeof(Ownership), typeof(OrganizationMembership), typeof(WorkspaceAccess)])
        {
            relation.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();
            relation.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.ReturnType == relation)
                .ShouldBeEmpty();
        }
    }

    [Fact]
    public void An_aggregate_references_another_only_by_identity()
    {
        // Workspace holds an OrganizationId, never an Organization instance (ADR-ARCH-014).
        var fields = typeof(Workspace)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(field => field.FieldType)
            .ToArray();

        fields.ShouldContain(typeof(OrganizationId));
        fields.ShouldNotContain(typeof(Organization));
    }

    [Fact]
    public void The_authorization_axis_lives_at_the_workspace_and_nowhere_else()
    {
        // ADR-ARCH-013: BP-001 authorizes at the Workspace grain. The organization holds no access
        // edge, so no code path can accidentally read an organization to decide where someone acts.
        var organizationFields = typeof(Organization)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(field => field.FieldType)
            .ToArray();

        organizationFields.ShouldNotContain(typeof(List<WorkspaceAccess>));
        organizationFields.ShouldNotContain(typeof(WorkspaceAccess));
    }

    [Fact]
    public void The_three_axes_stay_distinct_facts_about_the_same_founder()
    {
        // Structurally absorbed, semantically independent: the founding act produces ownership,
        // membership and access as three separate records — none derived from another
        // (owns → access and member_of → access remain forbidden).
        var founder = new PersonId(Guid.CreateVersion7());

        var organization = Organization.Found("Bâtiment Abidjan", founder, FoundingInstant);
        var workspace = Workspace.Found(
            organization.Id, "Agence Cocody", founder, FoundingInstant);

        organization.Ownership.Owner.ShouldBe(founder);
        organization.IsMember(founder).ShouldBeTrue();
        workspace.GrantsAccessTo(founder).ShouldBeTrue();
        workspace.OrganizationId.ShouldBe(organization.Id);
    }

    [Fact]
    public void No_aggregate_raises_a_domain_event()
    {
        // BP-001 assigns none. Asserted across both roots so a later slice cannot introduce one
        // silently in the aggregate that happens to be edited.
        var founder = new PersonId(Guid.CreateVersion7());

        Organization.Found("Bâtiment Abidjan", founder, FoundingInstant).DomainEvents.ShouldBeEmpty();
        Workspace.Found(OrganizationId.New(), "Agence Cocody", founder, FoundingInstant)
            .DomainEvents.ShouldBeEmpty();
    }
}
