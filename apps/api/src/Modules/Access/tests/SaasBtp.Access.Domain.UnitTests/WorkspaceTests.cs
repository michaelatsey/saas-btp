using System.Reflection;
using SaasBtp.Access.Domain.Identity;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;
using SaasBtp.Access.Domain.Workspaces.Rules;
using Shouldly;
using Xunit;

namespace SaasBtp.Access.Domain.UnitTests;

/// <summary>
/// The Workspace aggregate: the consistency boundary of the AUTHORIZATION axis. It belongs to an
/// organization BY IDENTITY (two roots, ADR-ARCH-014), holds its access edges as internal components
/// (ADR-ARCH-016), and owns no uniqueness rule on its name.
/// </summary>
public sealed class WorkspaceTests
{
    private static readonly DateTime FoundingInstant = new(2026, 8, 16, 10, 30, 0, DateTimeKind.Utc);

    private static PersonId SomePerson() => new(Guid.CreateVersion7());

    private static Workspace Found(OrganizationId organizationId, PersonId founder) =>
        Workspace.Found(organizationId, "Agence Cocody", founder, FoundingInstant);

    [Fact]
    public void A_founded_workspace_authorizes_its_founder_by_construction()
    {
        var founder = SomePerson();

        var workspace = Found(OrganizationId.New(), founder);

        workspace.GrantsAccessTo(founder).ShouldBeTrue();
        workspace.Accesses.Count.ShouldBe(1);
    }

    [Fact]
    public void A_workspace_cannot_be_founded_without_an_identified_person_to_authorize()
        => BusinessRuleAssertions.ShouldBreakRule<WorkspaceAccessMustIdentifyAPersonRule>(
            () => Found(OrganizationId.New(), default));

    [Fact]
    public void No_workspace_can_exist_unauthorized_because_no_path_leads_there()
    {
        // Structural, like the organization's ownership: the founder's edge is established inside
        // construction. There is no Create() + GrantInitialAccess() sequence to get wrong, and no
        // public grant or revoke — the FORM of revocability is deferred (Implementation Design §3.5)
        // and this asserts nothing has quietly decided it.
        var members = typeof(Workspace)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToArray();

        members.ShouldContain("Found");
        members.ShouldNotContain("Create");
        members.ShouldNotContain("Grant");
        members.ShouldNotContain("Revoke");
    }

    [Fact]
    public void Authorization_is_binary_a_duplicate_access_is_refused()
    {
        // The rule the aggregate checks on the single path that grants an edge. Stated on the rule
        // itself because the aggregate publishes no second grant path today (nothing in BP-001
        // mutates an existing workspace) — the process that grants access later inherits this guard
        // by going through that same path.
        var alreadyAuthorized = SomePerson();

        new WorkspaceAccessMustBeUniquePerPersonRule([alreadyAuthorized], alreadyAuthorized)
            .IsBroken().ShouldBeTrue();
        new WorkspaceAccessMustBeUniquePerPersonRule([alreadyAuthorized], SomePerson())
            .IsBroken().ShouldBeFalse();
        new WorkspaceAccessMustBeUniquePerPersonRule([], alreadyAuthorized)
            .IsBroken().ShouldBeFalse();
    }

    [Fact]
    public void A_person_with_no_edge_here_is_not_authorized_here()
    {
        var workspace = Found(OrganizationId.New(), SomePerson());

        workspace.GrantsAccessTo(SomePerson()).ShouldBeFalse();
    }

    [Fact]
    public void The_access_edge_binds_a_person_to_THIS_workspace_never_to_an_organization()
    {
        // The single access edge BP-001 produces sits at the Workspace grain (ADR-ARCH-013). The
        // edge no longer names a workspace at all: it lives inside the one it authorizes, which is
        // a stronger statement than a parameter could make.
        var accessMembers = typeof(WorkspaceAccess)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.PropertyType)
            .ToArray();

        accessMembers.ShouldNotContain(typeof(OrganizationId));
        accessMembers.ShouldContain(typeof(PersonId));
    }

    [Fact]
    public void A_workspace_without_an_organization_is_refused()
        => BusinessRuleAssertions.ShouldBreakRule<WorkspaceMustBelongToAnOrganizationRule>(
            () => Found(default, SomePerson()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_workspace_without_a_name_is_refused(string? blank)
        => BusinessRuleAssertions.ShouldBreakRule<WorkspaceNameMustBeProvidedRule>(
            () => Workspace.Found(OrganizationId.New(), blank!, SomePerson(), FoundingInstant));

    [Fact]
    public void A_workspace_references_its_organization_by_identity_and_holds_no_instance_of_it()
    {
        var organizationId = OrganizationId.New();

        var workspace = Found(organizationId, SomePerson());

        workspace.OrganizationId.ShouldBe(organizationId);
        workspace.Id.Value.ShouldNotBe(organizationId.Value);
        typeof(Workspace).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ShouldNotContain(property => property.PropertyType == typeof(Organization));
    }

    [Fact]
    public void Two_workspaces_of_the_same_organization_may_share_a_name()
    {
        // The FORM of workspace resolution is left open by the business model, so no uniqueness rule
        // exists here — and none exists in the schema either. Asserted so a future "helpful" rule
        // cannot be added without this test failing.
        var organizationId = OrganizationId.New();
        var founder = SomePerson();

        var first = Found(organizationId, founder);
        var second = Found(organizationId, founder);

        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public void The_supplied_instant_stamps_the_workspace_and_the_access_it_establishes()
    {
        var workspace = Found(OrganizationId.New(), SomePerson());

        workspace.CreatedAtUtc.ShouldBe(FoundingInstant);
        workspace.Accesses.Single().GrantedAtUtc.ShouldBe(FoundingInstant);
    }

    [Fact]
    public void A_non_utc_instant_is_a_caller_bug_not_a_business_rule()
        => Should.Throw<ArgumentException>(() => Workspace.Found(
            OrganizationId.New(), "A", SomePerson(),
            new DateTime(2026, 8, 16, 10, 30, 0, DateTimeKind.Local)));

    [Fact]
    public void The_accesses_cannot_be_mutated_from_outside_the_aggregate()
        => ((ICollection<WorkspaceAccess>)Found(OrganizationId.New(), SomePerson()).Accesses)
            .IsReadOnly.ShouldBeTrue();

    [Fact]
    public void The_founding_workspace_raises_no_domain_event()
        => Found(OrganizationId.New(), SomePerson()).DomainEvents.ShouldBeEmpty();
}
