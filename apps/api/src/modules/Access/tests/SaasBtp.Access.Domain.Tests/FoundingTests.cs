using MicroKit.Domain.Aggregates;
using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Domain.Tests;

/// <summary>
/// The founding as one operation: the audit of what it creates (sql.md "Audit / lifecycle") and
/// the domain event "Company founded" (contract.md §1).
/// </summary>
public sealed class FoundingTests
{
    [Fact]
    public void Audit_CreatedAtComesFromTheInjectedClock()
    {
        var user = Founder.NewUser();

        var founded = Founder.Found(user);

        IAuditableEntity[] created =
        [
            founded.Profile, founded.Organization, founded.Workspace,
            founded.Ownership, founded.Membership, founded.Access,
        ];
        Assert.All(created, aggregate =>
        {
            Assert.Equal(Founder.Now, aggregate.CreatedAt);
            Assert.Equal(user.Value.ToString(), aggregate.CreatedBy.Value);
            Assert.Null(aggregate.UpdatedAt);
            Assert.Null(aggregate.UpdatedBy);
        });
    }

    [Fact]
    public void Audit_EveryAggregateOfTheFoundingIsAuditable()
    {
        var aggregates = typeof(CompanyFounding).Assembly.GetExportedTypes()
            .Where(type => typeof(IAggregateRoot).IsAssignableFrom(type))
            .ToList();

        Assert.Equal(6, aggregates.Count);
        Assert.All(aggregates, aggregate =>
        {
            Assert.True(aggregate.IsSealed, $"{aggregate.Name} is not sealed.");
            Assert.True(typeof(IAuditableEntity).IsAssignableFrom(aggregate), $"{aggregate.Name} is not auditable.");
        });
    }

    public static readonly TheoryData<string> NullArguments = new("organizationName", "workspaceName", "clock");

    [Theory]
    [MemberData(nameof(NullArguments))]
    public void Founding_RefusesANullArgument(string argument)
    {
        var exception = Assert.Throws<ArgumentNullException>(() => CompanyFounding.Found(
            Founder.NewReference(),
            argument == "organizationName" ? null! : Founder.OrganizationName(),
            argument == "workspaceName" ? null! : Founder.WorkspaceName(),
            Founder.NewUser(),
            null,
            argument == "clock" ? null! : Founder.Clock()));

        Assert.Equal(argument, exception.ParamName);
    }

    [Fact]
    public void CompanyFounded_IsRaisedOnceByTheFoundedOrganization()
    {
        var founded = Founder.Found(Founder.NewUser());

        var raised = Assert.Single(founded.Organization.DomainEvents);
        var companyFounded = Assert.IsType<CompanyFounded>(raised);
        Assert.Equal(founded.Organization.Id, companyFounded.OrganizationId);
        Assert.Equal(founded.Workspace.Id, companyFounded.WorkspaceId);
        Assert.Equal(founded.Profile.Id, companyFounded.FounderProfileId);
        Assert.Equal(Founder.Now, companyFounded.OccurredAt);

        Assert.Empty(founded.Profile.DomainEvents);
        Assert.Empty(founded.Workspace.DomainEvents);
        Assert.Empty(founded.Ownership.DomainEvents);
        Assert.Empty(founded.Membership.DomainEvents);
        Assert.Empty(founded.Access.DomainEvents);
    }
}
