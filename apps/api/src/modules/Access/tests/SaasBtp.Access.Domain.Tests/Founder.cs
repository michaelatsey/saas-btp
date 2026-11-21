using Microsoft.Extensions.Time.Testing;
using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Domain.Tests;

/// <summary>
/// Builds the inputs of a founding: a fixed clock, a signed-in person and a request, as TSK-2 of
/// BP-001 receives them.
/// </summary>
internal static class Founder
{
    /// <summary>The instant every founding of these tests reads from its clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 30, 15, 123, TimeSpan.Zero);

    public static FakeTimeProvider Clock() => new(Now);

    public static UserId NewUser() => new(Guid.NewGuid());

    public static OrganizationId NewReference() => OrganizationId.New(Now);

    public static OrganizationName OrganizationName(string declared = "Kouassi BTP") =>
        Organizations.OrganizationName.TryCreate(declared, out var name, out _)
            ? name
            : throw new ArgumentException($"'{declared}' is not a valid organization name.", nameof(declared));

    public static WorkspaceName WorkspaceName(string declared = "Siège Abidjan") =>
        Workspaces.WorkspaceName.TryCreate(declared, out var name, out _)
            ? name
            : throw new ArgumentException($"'{declared}' is not a valid workspace name.", nameof(declared));

    /// <summary>A founding by <paramref name="user"/>, with their profile when they have one.</summary>
    public static FoundedCompany Found(
        UserId user,
        Profile? profile = null,
        OrganizationId? reference = null,
        string organizationName = "Kouassi BTP",
        string workspaceName = "Siège Abidjan") =>
        CompanyFounding.Found(
            reference ?? NewReference(),
            OrganizationName(organizationName),
            WorkspaceName(workspaceName),
            user,
            profile,
            Clock());
}
