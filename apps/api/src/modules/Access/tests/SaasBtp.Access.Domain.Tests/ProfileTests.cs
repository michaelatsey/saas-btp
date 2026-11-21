using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Profiles;
using MicroKit.Domain.ValueObjects.Common;

namespace SaasBtp.Access.Domain.Tests;

/// <summary>
/// The founder's profile, and a founder who already takes part in another organization
/// (INV-ACCESS-01, BP001-R1, BP001-R2, BP001-AC-03, BP001-AC-04).
/// </summary>
public sealed class ProfileTests
{
    [Fact]
    public void INV_ACCESS_01_FoundingCreatesAProfileOnlyWhenTheFounderHasNone()
    {
        var user = Founder.NewUser();

        var first = Founder.Found(user);
        var second = Founder.Found(user, first.Profile);

        Assert.True(first.IsProfileCreated);
        Assert.Equal(user, first.Profile.UserId);
        Assert.False(second.IsProfileCreated);
        Assert.Same(first.Profile, second.Profile);
    }

    [Fact]
    public void INV_ACCESS_01_FoundingRefusesTheProfileOfAnotherPerson()
    {
        var someoneElse = Founder.Found(Founder.NewUser()).Profile;

        Assert.Throws<ArgumentException>(() => Founder.Found(Founder.NewUser(), someoneElse));
    }

    [Fact]
    public void BP001_AC_03_OwnerOfAnotherOrganizationFoundsWithTheSameProfileAndKeepsTheirRelations()
    {
        var user = Founder.NewUser();
        var first = Founder.Found(user, organizationName: "Kouassi BTP");
        var before = Snapshot(first);

        var second = Founder.Found(user, first.Profile, organizationName: "Kouassi Maintenance", workspaceName: "Kouassi Maintenance");

        Assert.Equal(before, Snapshot(first));
        Assert.Equal(first.Profile.Id, second.Ownership.ProfileId);
        Assert.Equal(first.Profile.Id, second.Membership.ProfileId);
        Assert.Equal(first.Profile.Id, second.Access.ProfileId);
        Assert.NotEqual(first.Organization.Id, second.Organization.Id);
        Assert.Equal(second.Organization.Id, second.Ownership.OrganizationId);
        Assert.Equal(second.Organization.Id, second.Membership.OrganizationId);
        Assert.Equal(second.Workspace.Id, second.Access.WorkspaceId);
        Assert.NotEqual(first.Workspace.Id, second.Workspace.Id);
    }

    [Fact]
    public void BP001_AC_04_MemberOfAnotherOrganizationFoundsWithTheSameProfileAndKeepsTheirMembership()
    {
        var owner = Founder.Found(Founder.NewUser(), organizationName: "Kouassi BTP");
        var user = Founder.NewUser();
        var profile = new Profile(ProfileId.New(Founder.Now), user, Founder.Now, new Actor(user.Value.ToString()));
        var membership = new Membership(
            MembershipId.New(Founder.Now), profile.Id, owner.Organization.Id, Founder.Now, new Actor(owner.Profile.UserId.Value.ToString()));
        var before = (membership.Id, membership.ProfileId, membership.OrganizationId, membership.EndedAt, membership.CreatedAt, membership.UpdatedAt);

        var founded = Founder.Found(user, profile, organizationName: "Diallo Construction");

        Assert.Equal(before, (membership.Id, membership.ProfileId, membership.OrganizationId, membership.EndedAt, membership.CreatedAt, membership.UpdatedAt));
        Assert.False(founded.IsProfileCreated);
        Assert.Equal(profile.Id, founded.Ownership.ProfileId);
        Assert.Equal(founded.Organization.Id, founded.Ownership.OrganizationId);
        Assert.Equal(founded.Organization.Id, founded.Membership.OrganizationId);
        Assert.Equal(founded.Organization.Id, founded.Access.OrganizationId);
        Assert.NotEqual(owner.Organization.Id, founded.Organization.Id);
    }

    private static object Snapshot(Founding.FoundedCompany founded) => (
        (founded.Organization.Id, founded.Organization.Name.Value, founded.Organization.Status, founded.Organization.CurrentOwnershipId, founded.Organization.UpdatedAt),
        (founded.Workspace.Id, founded.Workspace.OrganizationId, founded.Workspace.Name.Value, founded.Workspace.UpdatedAt),
        (founded.Ownership.Id, founded.Ownership.ProfileId, founded.Ownership.OrganizationId, founded.Ownership.UpdatedAt),
        (founded.Membership.Id, founded.Membership.ProfileId, founded.Membership.OrganizationId, founded.Membership.EndedAt, founded.Membership.UpdatedAt),
        (founded.Access.Id, founded.Access.ProfileId, founded.Access.WorkspaceId, founded.Access.OrganizationId, founded.Access.EndedAt, founded.Access.UpdatedAt));

    [Fact]
    public void INV_ACCESS_01_ProfileRefusesADefaultUser()
    {
        Assert.Throws<ArgumentException>(() =>
            new Profile(ProfileId.New(Founder.Now), default, Founder.Now, new Actor("someone")));
    }
}
