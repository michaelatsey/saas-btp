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
/// "nothing written" reads as "never called". A test can make it find the reference in use, say
/// which organization the caller currently owns, or fail.
/// </summary>
internal sealed class FakeFoundingStore : IFoundingStore
{
    private readonly List<(OrganizationId Reference, OrganizationName OrganizationName, WorkspaceName WorkspaceName, UserId Founder)> _calls = [];

    private readonly List<(OrganizationId Reference, UserId Owner)> _ownedReads = [];

    public IReadOnlyList<(OrganizationId Reference, OrganizationName OrganizationName, WorkspaceName WorkspaceName, UserId Founder)> Calls => _calls;

    /// <summary>The current-ownership reads asked for, in order.</summary>
    public IReadOnlyList<(OrganizationId Reference, UserId Owner)> OwnedReads => _ownedReads;

    /// <summary>When true, a founding finds its reference already in use.</summary>
    public bool ReferenceInUse { get; init; }

    /// <summary>What the current-ownership read finds.</summary>
    public Organization? Owned { get; init; }

    /// <summary>When set, a founding throws it.</summary>
    public Exception? FoundingFailure { get; init; }

    /// <summary>When set, the current-ownership read throws it.</summary>
    public Exception? ReadFailure { get; init; }

    public Task<FoundingAttempt> FoundAsync(
        OrganizationId reference,
        OrganizationName organizationName,
        WorkspaceName workspaceName,
        UserId founder,
        CancellationToken cancellationToken = default)
    {
        _calls.Add((reference, organizationName, workspaceName, founder));
        if (FoundingFailure is not null)
        {
            return Task.FromException<FoundingAttempt>(FoundingFailure);
        }

        return Task.FromResult<FoundingAttempt>(ReferenceInUse
            ? new FoundingAttempt.ReferenceInUse()
            : new FoundingAttempt.Founded(CompanyFounding.Found(reference, organizationName, workspaceName, founder, founderProfile: null, TimeProvider.System)));
    }

    public Task<Organization?> FindOwnedAsync(OrganizationId reference, UserId owner, CancellationToken cancellationToken = default)
    {
        _ownedReads.Add((reference, owner));
        return ReadFailure is not null ? Task.FromException<Organization?>(ReadFailure) : Task.FromResult(Owned);
    }
}
