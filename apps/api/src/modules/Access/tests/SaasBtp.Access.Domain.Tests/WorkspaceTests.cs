using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Domain.Tests;

/// <summary>The first workspace of a founding, and the founder's access to it (INV-ACCESS-03, BP001-R4).</summary>
public sealed class WorkspaceTests
{
    [Fact]
    public void INV_ACCESS_03_FoundingYieldsExactlyOneWorkspaceOfTheFoundedOrganization()
    {
        var reference = Founder.NewReference();

        var founded = Founder.Found(Founder.NewUser(), reference: reference, workspaceName: "Siège Abidjan");

        Assert.Equal(reference, founded.Workspace.OrganizationId);
        Assert.Equal("Siège Abidjan", founded.Workspace.Name.Value);

        var properties = typeof(FoundedCompany).GetProperties();
        Assert.Single(properties, property => property.PropertyType == typeof(Workspace));
        Assert.DoesNotContain(properties, property =>
            property.PropertyType != typeof(string)
            && property.PropertyType.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                && contract.GetGenericArguments()[0] == typeof(Workspace)));
    }

    [Fact]
    public void BP001_AC_05_FounderAccessNamesExactlyTheFirstWorkspace()
    {
        var founded = Founder.Found(Founder.NewUser());

        Assert.Equal(founded.Workspace.Id, founded.Access.WorkspaceId);
        Assert.Equal(founded.Workspace.OrganizationId, founded.Access.OrganizationId);
        Assert.Equal(founded.Organization.Id, founded.Access.OrganizationId);
        Assert.Equal(founded.Profile.Id, founded.Access.ProfileId);
        Assert.Single(typeof(FoundedCompany).GetProperties(), property => property.PropertyType == typeof(WorkspaceAccesses.WorkspaceAccess));
    }
}
