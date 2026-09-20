using System.Reflection;
using SaasBtp.Access.Domain.Identity;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Organizations.Rules;
using Shouldly;
using Xunit;

namespace SaasBtp.Access.Domain.UnitTests;

/// <summary>
/// The Organization aggregate: the consistency boundary of the PROPERTY and BELONGING axes. These
/// tests assert what the aggregate GUARANTEES — that a founded organization always has exactly one
/// owner and a binary membership — and that those guarantees are structural, not left to a caller's
/// discipline or to a database constraint.
/// </summary>
public sealed class OrganizationTests
{
    private static readonly DateTime FoundingInstant = new(2026, 8, 16, 10, 30, 0, DateTimeKind.Utc);

    private static PersonId SomePerson() => new(Guid.CreateVersion7());

    private static Organization Found(PersonId founder) =>
        Organization.Found("Bâtiment Abidjan", founder, FoundingInstant);

    [Fact]
    public void A_founded_organization_has_exactly_one_owner_and_it_is_its_founder()
    {
        var founder = SomePerson();

        var organization = Found(founder);

        organization.Ownership.Owner.ShouldBe(founder);
    }

    [Fact]
    public void An_organization_cannot_be_founded_without_an_identified_owner()
        => BusinessRuleAssertions.ShouldBreakRule<OrganizationMustHaveAnIdentifiedOwnerRule>(
            () => Found(default));

    [Fact]
    public void No_organization_can_exist_ownerless_or_twice_owned_because_no_path_leads_there()
    {
        // The invariant is STRUCTURAL, which is the whole point of a founding factory: ownership is
        // established inside construction, and the aggregate publishes no way to assign, replace or
        // add one afterwards. A sequence of Create() then AssignInitialOwner() would leave a valid
        // but ownerless organization between the two calls; this asserts that shape cannot come back.
        var mutators = typeof(Organization)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToArray();

        mutators.ShouldNotContain(name => name.Contains("Owner", StringComparison.Ordinal));
        mutators.ShouldContain("Found");
        mutators.ShouldNotContain("Create");
    }

    [Fact]
    public void The_founder_is_also_the_first_member_established_by_construction()
    {
        var founder = SomePerson();

        var organization = Found(founder);

        organization.IsMember(founder).ShouldBeTrue();
        organization.Memberships.Count.ShouldBe(1);
    }

    [Fact]
    public void Belonging_is_binary_a_duplicate_member_is_refused()
    {
        // The rule the aggregate checks on the single path that admits anyone. Stated on the rule
        // itself because the aggregate publishes no second admission path today (nothing in BP-001
        // mutates an existing organization) — the process that admits members later inherits this
        // guard by going through that same path.
        var alreadyIn = SomePerson();

        new MembershipMustBeUniquePerPersonRule([alreadyIn], alreadyIn).IsBroken().ShouldBeTrue();
        new MembershipMustBeUniquePerPersonRule([alreadyIn], SomePerson()).IsBroken().ShouldBeFalse();
        new MembershipMustBeUniquePerPersonRule([], alreadyIn).IsBroken().ShouldBeFalse();
    }

    [Fact]
    public void Owner_and_member_are_two_facts_about_the_same_person_never_one()
    {
        // Correlated by the founding act, derived from one another by nothing: the founder is owner
        // AND member, and the aggregate records both separately (ADR-ARCH-013/016).
        var founder = SomePerson();

        var organization = Found(founder);

        organization.Ownership.Owner.ShouldBe(founder);
        organization.IsMember(founder).ShouldBeTrue();
        organization.Memberships.Single().Member.ShouldBe(organization.Ownership.Owner);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_organization_without_a_declared_name_is_refused(string? blank)
        => BusinessRuleAssertions.ShouldBreakRule<OrganizationNameMustBeProvidedRule>(
            () => Organization.Found(blank!, SomePerson(), FoundingInstant));

    [Fact]
    public void A_founded_organization_is_declared()
        => Found(SomePerson()).Status.ShouldBe(OrganizationStatus.Declared);

    [Fact]
    public void The_status_cannot_be_chosen_by_the_caller()
    {
        // Enforced STRUCTURALLY: the factory exposes no status parameter, so no caller can express
        // another one. Asserting the shape of the contract is asserting what actually guarantees it.
        var found = typeof(Organization).GetMethod(nameof(Organization.Found));

        found.ShouldNotBeNull();
        found!.GetParameters()
            .ShouldNotContain(parameter => parameter.ParameterType == typeof(OrganizationStatus));
    }

    [Fact]
    public void The_supplied_instant_stamps_the_organization_and_every_fact_it_establishes()
    {
        var organization = Found(SomePerson());

        organization.CreatedAtUtc.ShouldBe(FoundingInstant);
        organization.Ownership.EstablishedAtUtc.ShouldBe(FoundingInstant);
        organization.Memberships.Single().JoinedAtUtc.ShouldBe(FoundingInstant);
    }

    [Fact]
    public void A_non_utc_founding_instant_is_a_caller_bug_not_a_business_rule()
    {
        // Preconditions throw ArgumentException; invariants throw BusinessRuleViolationException.
        // Keeping the two distinguishable is the point.
        Should.Throw<ArgumentException>(() => Organization.Found(
            "A", SomePerson(), new DateTime(2026, 8, 16, 10, 30, 0, DateTimeKind.Local)));
        Should.Throw<ArgumentException>(() => Organization.Found(
            "A", SomePerson(), new DateTime(2026, 8, 16, 10, 30, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void A_declared_organization_is_founded_with_a_fresh_identity()
        => Found(SomePerson()).Id.ShouldNotBe(Found(SomePerson()).Id);

    [Fact]
    public void Two_organizations_are_the_same_when_their_identity_is_the_same()
    {
        // Entity equality: identity, never state (MicroKit Entity<TId>).
        var organization = Found(SomePerson());

        organization.ShouldBe(organization);
        organization.ShouldNotBe(Found(SomePerson()));
    }

    [Fact]
    public void The_memberships_cannot_be_mutated_from_outside_the_aggregate()
        => ((ICollection<OrganizationMembership>)Found(SomePerson()).Memberships)
            .IsReadOnly.ShouldBeTrue();

    [Fact]
    public void The_founding_organization_raises_no_domain_event()
        // BP-001 assigns none: the specification's EVTs are transitions of the PROCESS state model,
        // not events the aggregates publish. Introducing one would be a new decision.
        => Found(SomePerson()).DomainEvents.ShouldBeEmpty();
}
