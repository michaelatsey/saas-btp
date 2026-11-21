using SaasBtp.Access.Domain.Organizations;

namespace SaasBtp.Access.Domain.Tests;

/// <summary>The organization a founding creates (INV-ACCESS-08, ADR-PROD-002).</summary>
public sealed class OrganizationTests
{
    [Fact]
    public void INV_ACCESS_08_FoundedOrganizationIsDeclared()
    {
        var founded = Founder.Found(Founder.NewUser());

        Assert.Equal(OrganizationStatus.Declared, founded.Organization.Status);
    }

    [Fact]
    public void INV_ACCESS_08_OrganizationOffersNoOperationThatChangesItsStatus()
    {
        var status = typeof(Organization).GetProperty(nameof(Organization.Status))!;

        Assert.False(Members.IsSettableOutside(status), "Organization.Status can be set from outside the type.");
        Assert.Empty(Members.Operations(typeof(Organization)));
    }

    [Fact]
    public void INV_ACCESS_08_FoundedOrganizationKeepsTheDeclaredName()
    {
        var founded = Founder.Found(Founder.NewUser(), organizationName: "  Kouassi BTP  ");

        Assert.Equal("Kouassi BTP", founded.Organization.Name.Value);
    }
}
