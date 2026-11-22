using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.Workspaces;
using SaasBtp.Access.Features.Ports;

namespace SaasBtp.Access.Features.Tests;

/// <summary>The caller port, set by the test.</summary>
internal sealed class FakeCaller(Caller? current) : ICaller
{
    public Caller? Current { get; } = current;

    /// <summary>A signed-in person who holds the confirmed-contact level.</summary>
    public static FakeCaller Confirmed() => new(new Caller(new UserId(Guid.NewGuid()), HasConfirmedContact: true));

    /// <summary>A signed-in person without the level.</summary>
    public static FakeCaller Unconfirmed() => new(new Caller(new UserId(Guid.NewGuid()), HasConfirmedContact: false));
}

/// <summary>
/// The founding port: it records every founding it is asked for and founds it in memory, so that
/// "nothing written" reads as "never called".
/// </summary>
internal sealed class FakeFoundingUnitOfWork : IFoundingUnitOfWork
{
    private readonly List<(OrganizationId Reference, OrganizationName OrganizationName, WorkspaceName WorkspaceName, UserId Founder)> _calls = [];

    public IReadOnlyList<(OrganizationId Reference, OrganizationName OrganizationName, WorkspaceName WorkspaceName, UserId Founder)> Calls => _calls;

    public Task<FoundedCompany> FoundAsync(
        OrganizationId reference,
        OrganizationName organizationName,
        WorkspaceName workspaceName,
        UserId founder,
        CancellationToken cancellationToken = default)
    {
        _calls.Add((reference, organizationName, workspaceName, founder));
        return Task.FromResult(CompanyFounding.Found(reference, organizationName, workspaceName, founder, founderProfile: null, TimeProvider.System));
    }
}
