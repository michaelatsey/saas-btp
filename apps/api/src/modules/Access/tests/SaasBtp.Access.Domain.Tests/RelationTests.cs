using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.WorkspaceAccesses;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Domain.Tests;

/// <summary>The three relations a founding creates (INV-ACCESS-05, INV-ACCESS-06, INV-ACCESS-07).</summary>
public sealed class RelationTests
{
    private static readonly Type[] Relations = [typeof(Ownership), typeof(Membership), typeof(WorkspaceAccess)];

    [Fact]
    public void INV_ACCESS_05_FoundingYieldsOneOwnershipOneMembershipAndOneAccessAsDistinctObjects()
    {
        var founded = Founder.Found(Founder.NewUser());

        object[] relations = [founded.Ownership, founded.Membership, founded.Access];
        Assert.Equal(3, relations.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(3, new[] { founded.Ownership.Id.Value, founded.Membership.Id.Value, founded.Access.Id.Value }.Distinct().Count());

        Assert.Equal(founded.Profile.Id, founded.Ownership.ProfileId);
        Assert.Equal(founded.Profile.Id, founded.Membership.ProfileId);
        Assert.Equal(founded.Profile.Id, founded.Access.ProfileId);
        Assert.Equal(founded.Organization.Id, founded.Ownership.OrganizationId);
        Assert.Equal(founded.Organization.Id, founded.Membership.OrganizationId);
        Assert.Equal(founded.Organization.Id, founded.Access.OrganizationId);
        Assert.Equal(founded.Ownership.Id, founded.Organization.CurrentOwnershipId);
    }

    [Fact]
    public void INV_ACCESS_05_NoRelationIsCreatedFromAnother()
    {
        var derivations = Relations
            .SelectMany(relation => Members.Callables(relation)
                .SelectMany(callable => callable.GetParameters()
                    .Where(parameter => Relations.Contains(parameter.ParameterType))
                    .Select(parameter => $"{relation.Name}.{callable.Name} takes a {parameter.ParameterType.Name}")))
            .ToList();

        Assert.Empty(derivations);
    }

    [Fact]
    public void INV_ACCESS_05_OwnershipAndMembershipGrantNoAccess()
    {
        Type[] access = [typeof(WorkspaceAccess), typeof(WorkspaceId), typeof(WorkspaceId?)];
        Type[] relations = [typeof(Ownership), typeof(Membership)];

        // Every property each type declares, auto-properties included, and every method that is
        // not a property accessor: what either could expose.
        var members = relations
            .SelectMany(relation => relation.GetProperties(Members.Declared)
                .Select(property => (Relation: relation, Name: property.Name, Type: property.PropertyType))
                .Concat(Members.Callables(relation)
                    .OfType<System.Reflection.MethodInfo>()
                    .Where(method => !method.IsSpecialName)
                    .Select(method => (Relation: relation, Name: method.Name, Type: method.ReturnType))))
            .ToList();
        Assert.NotEmpty(members);

        var grants = members
            .Where(member => access.Contains(member.Type))
            .Select(member => $"{member.Relation.Name}.{member.Name} is a {member.Type.Name}")
            .ToList();

        Assert.Empty(grants);
    }

    [Fact]
    public void INV_ACCESS_07_FoundedMembershipAndAccessCarryNoEnd()
    {
        var founded = Founder.Found(Founder.NewUser());

        Assert.Null(founded.Membership.EndedAt);
        Assert.Null(founded.Access.EndedAt);

        foreach (var relation in new[] { typeof(Membership), typeof(WorkspaceAccess) })
        {
            Assert.False(Members.IsSettableOutside(relation.GetProperty("EndedAt")!), $"{relation.Name}.EndedAt can be set from outside the type.");
            Assert.Empty(Members.Operations(relation));
        }
    }
}
