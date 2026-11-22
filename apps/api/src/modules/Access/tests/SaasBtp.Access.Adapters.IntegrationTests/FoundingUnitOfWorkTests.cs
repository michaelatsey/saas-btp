using System.Text.RegularExpressions;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SaasBtp.Access.Adapters.Persistence;
using SaasBtp.Access.Adapters.Persistence.Founding;
using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Ownerships;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.WorkspaceAccesses;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The founding written by the Access module's unit of work, through EF Core and the copied
/// MicroKit.Persistence, as <c>saasbtp_api</c>: the Integration proofs contract.md §7 sets for
/// INV-ACCESS-03, INV-ACCESS-05 and BP001-AC-17, and the order of ADR-ARCH-033 §5. That an access
/// naming another organization's workspace grants no sight of it before rollback, the other case
/// ADR-ARCH-033 §5 requires, stays proven in SQL by
/// <see cref="FoundingOrderTests.Adr033_AnAccessNamingAnotherOrganizationsWorkspaceGrantsNoSightBeforeRollback"/>.
/// </summary>
public sealed partial class FoundingUnitOfWorkTests(AccessDatabase database)
{
    /// <summary>The tables of a founding in the order of ADR-ARCH-033 §5.</summary>
    private static readonly string[] Order =
        ["profiles", "ownerships", "organizations", "memberships", "workspace_accesses", "workspaces"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task INV_ACCESS_03_AFoundingLeavesTheOrganizationHoldingItsFirstWorkspace()
    {
        var request = FoundingRequest.New();

        var founded = await FoundAsync(request);

        Assert.Equal(
            [(founded.Workspace.Id.Value, request.WorkspaceName.Value)],
            await WorkspacesOfAsync(database.Superuser, request.Reference));

        await using var founder = await database.AsAsync(request.User);
        Assert.Equal(1L, await founder.CountAsync(
            "select count(*) from access.workspaces where organization_id = @organization and id = @workspace",
            ("organization", request.Reference.Value), ("workspace", founded.Workspace.Id.Value)));
    }

    [Fact]
    public async Task INV_ACCESS_05_AFoundingLeavesThreeRelationsAndNoOtherAccess()
    {
        var request = FoundingRequest.New();

        var founded = await FoundAsync(request);

        var profile = founded.Profile.Id.Value;
        var organization = request.Reference.Value;
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.ownerships where organization_id = @organization and profile_id = @profile",
            ("organization", organization), ("profile", profile)));
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.ownerships where organization_id = @organization", ("organization", organization)));
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.memberships where organization_id = @organization and profile_id = @profile",
            ("organization", organization), ("profile", profile)));
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.memberships where organization_id = @organization", ("organization", organization)));
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.workspace_accesses where profile_id = @profile and workspace_id = @workspace and organization_id = @organization",
            ("profile", profile), ("workspace", founded.Workspace.Id.Value), ("organization", organization)));
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.workspace_accesses where organization_id = @organization", ("organization", organization)));
        Assert.Equal(0L, await database.CountAsSuperuserAsync(
            "select count(*) from access.workspace_accesses where profile_id = @profile and workspace_id <> @workspace",
            ("profile", profile), ("workspace", founded.Workspace.Id.Value)));

        await using var founder = await database.AsAsync(request.User);
        Assert.True(await founder.MayActInAsync(founded.Workspace.Id.Value));
    }

    [Fact]
    public async Task BP001_AC_17_AFailureBeforeCommitLeavesNothingAndTheSameFoundingThenSucceeds()
    {
        var request = FoundingRequest.New();

        // The failure is forced inside the transaction the founding's unit of work owns, after its
        // last save and before COMMIT: nothing wraps FoundAsync.
        await using (var failing = ModuleServices.Start(
            database.Api, services => services.ConfigureDbContext<AccessDbContext>(options => options.AddInterceptors(new FailingCommit()))))
        {
            failing.Subject.Current = request.User;
            await using var scope = failing.Scope();
            var founding = scope.ServiceProvider.GetRequiredService<FoundingUnitOfWork>();

            await Assert.ThrowsAsync<ForcedFailureException>(() =>
                founding.FoundAsync(request.Reference, request.OrganizationName, request.WorkspaceName, request.Founder, Token));

            Assert.Equal(Order, TablesOf(failing.Commands.Inserts));
        }

        await AssertNothingOfAsync(request);
        Assert.Equal(0L, await database.CountAsSuperuserAsync(
            "select count(*) from access.profiles where user_id = @user", ("user", request.User)));

        var founded = await FoundAsync(request);

        Assert.Equal(request.Reference, founded.Organization.Id);
        Assert.True(founded.IsProfileCreated);
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.profiles where user_id = @user and id = @profile",
            ("user", request.User), ("profile", founded.Profile.Id.Value)));
        foreach (var (table, id) in RowsOf(founded))
        {
            Assert.Equal(1L, await database.CountAsSuperuserAsync($"select count(*) from access.{table} where id = @id", ("id", id)));
        }

        await using var founder = await database.AsAsync(request.User);
        Assert.Equal(Order, await SeenAsync(founder, founded));
    }

    [Fact]
    public void BP001_AC_17_AddAccessModuleRegistersNoHttpClient()
    {
        var services = new ServiceCollection();

        services.AddAccessModule(ModuleServices.Configuration(database.Api));

        // The full registration, persistence included, is what is inspected.
        Assert.Contains(services, service => service.ServiceType == typeof(AccessDbContext));
        Assert.DoesNotContain(services, service =>
            IsHttp(service.ServiceType) || IsHttp(service.ImplementationType) || IsHttp(service.ImplementationInstance?.GetType()));
    }

    [Fact]
    public async Task Adr033_TheFoundingInsertsInTheOrderOfSection5()
    {
        var request = FoundingRequest.New();
        await using var module = ModuleServices.Start(database.Api);
        module.Subject.Current = request.User;
        await using var scope = module.Scope();

        await scope.ServiceProvider.GetRequiredService<FoundingUnitOfWork>()
            .FoundCompanyAsync(request.Reference, request.OrganizationName, request.WorkspaceName, request.Founder, Token);

        // Six commands, each inserting one row: each row that must precede another is saved in its
        // own SaveChanges.
        var inserts = module.Commands.Inserts;
        Assert.All(inserts, insert => Assert.Single(InsertInto().Matches(insert)));
        Assert.Equal(Order, TablesOf(inserts));
    }

    /// <summary>
    /// The founding's other branch: a person who already has a profile founds again. The unit of work
    /// reads the profile under the subject, inserts no other, and saves the five other rows in order,
    /// changing nothing it read.
    /// </summary>
    [Fact]
    public async Task AFoundingByAPersonWithAProfileReusesItAndInsertsTheFiveOtherRows()
    {
        var first = FoundingRequest.New();
        var foundedFirst = await FoundAsync(first);
        var second = FoundingRequest.New() with { User = first.User };
        await using var module = ModuleServices.Start(database.Api);
        module.Subject.Current = second.User;
        await using var scope = module.Scope();

        var founded = await scope.ServiceProvider.GetRequiredService<FoundingUnitOfWork>()
            .FoundCompanyAsync(second.Reference, second.OrganizationName, second.WorkspaceName, second.Founder, Token);

        Assert.False(founded.IsProfileCreated);
        Assert.Equal(foundedFirst.Profile.Id, founded.Profile.Id);
        Assert.Equal(Order[1..], TablesOf(module.Commands.Inserts));
        Assert.DoesNotContain(module.Commands.Executed, command =>
            command.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || command.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1L, await database.CountAsSuperuserAsync(
            "select count(*) from access.profiles where user_id = @user", ("user", first.User)));

        await using var founder = await database.AsAsync(first.User);
        Assert.Equal(Order, await SeenAsync(founder, foundedFirst));
        Assert.Equal(Order, await SeenAsync(founder, founded));
    }

    [Fact]
    public async Task Adr033_TheFoundingReversedFails()
    {
        var request = FoundingRequest.New();
        await using var module = ModuleServices.Start(database.Api);
        module.Subject.Current = request.User;
        await using var scope = module.Scope();
        var services = scope.ServiceProvider;
        var unitOfWork = services.GetRequiredService<ITransactionalUnitOfWork>();
        var founded = CompanyFounding.Found(
            request.Reference, request.OrganizationName, request.WorkspaceName, request.Founder, founderProfile: null,
            services.GetRequiredService<TimeProvider>());

        // The rows of ADR-ARCH-033 §5 in the reverse order, through the same repositories and unit of work.
        Func<CancellationToken, ValueTask>[] reversed =
        [
            ct => services.GetRequiredService<IWorkspaceRepository>().AddAsync(founded.Workspace, ct),
            ct => services.GetRequiredService<IWorkspaceAccessRepository>().AddAsync(founded.Access, ct),
            ct => services.GetRequiredService<IMembershipRepository>().AddAsync(founded.Membership, ct),
            ct => services.GetRequiredService<IOrganizationRepository>().AddAsync(founded.Organization, ct),
            ct => services.GetRequiredService<IOwnershipRepository>().AddAsync(founded.Ownership, ct),
            ct => services.GetRequiredService<IProfileRepository>().AddAsync(founded.Profile, ct),
        ];

        var failure = await Assert.ThrowsAsync<PersistenceException>(() => unitOfWork.ExecuteAsync(
            static async (steps, ct) =>
            {
                foreach (var (add, commit) in steps)
                {
                    await add(ct);
                    await commit.CommitAsync(ct);
                }
            },
            reversed.Select(add => (add, (IUnitOfWork)unitOfWork)).ToArray(),
            Token));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(Innermost(failure)).SqlState);
        await AssertNothingOfAsync(request);
        foreach (var (table, id) in RowsOf(founded))
        {
            Assert.Equal(0L, await database.CountAsSuperuserAsync($"select count(*) from access.{table} where id = @id", ("id", id)));
        }
    }

    [Fact]
    public async Task NoValueIsGeneratedByTheDatabase()
    {
        // In the catalog: no default, identity or generated column on an Access table.
        var stored = await StoreSchema.ReadAsync(database.Superuser);
        Assert.Empty(stored.Columns.Where(column => column.Generated).Select(column => $"{column.Table}.{column.Name}"));

        // In the model: nothing EF would leave to the database.
        var request = FoundingRequest.New();
        await using var module = ModuleServices.Start(database.Api);
        module.Subject.Current = request.User;
        await using var scope = module.Scope();
        var model = scope.ServiceProvider.GetRequiredService<AccessDbContext>().Model;
        Assert.Empty(model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never
                || property.GetDefaultValueSql() is not null
                || property.TryGetDefaultValue(out _)
                || property.GetComputedColumnSql() is not null)
            .Select(property => $"{property.DeclaringType.ClrType.Name}.{property.Name}"));

        // After a founding: the identifiers and audit values stored are the domain's, from the
        // injected clock and the acting person.
        var founded = await scope.ServiceProvider.GetRequiredService<FoundingUnitOfWork>()
            .FoundCompanyAsync(request.Reference, request.OrganizationName, request.WorkspaceName, request.Founder, Token);

        var actor = new Actor(request.User.ToString()).Value;
        foreach (var (table, id) in RowsOf(founded))
        {
            var (createdAt, createdBy, updatedAt) = await AuditOfAsync(table, id);
            Assert.Equal(ModuleServices.Now, createdAt);
            Assert.Equal(actor, createdBy);
            Assert.Null(updatedAt);
        }
    }

    private static bool IsHttp(Type? type) =>
        type?.Namespace is { } name
        && (name.StartsWith("System.Net.Http", StringComparison.Ordinal) || name.StartsWith("Microsoft.Extensions.Http", StringComparison.Ordinal));

    private static IEnumerable<(string Table, Guid Id)> RowsOf(FoundedCompany founded) =>
    [
        ("profiles", founded.Profile.Id.Value),
        ("ownerships", founded.Ownership.Id.Value),
        ("organizations", founded.Organization.Id.Value),
        ("memberships", founded.Membership.Id.Value),
        ("workspace_accesses", founded.Access.Id.Value),
        ("workspaces", founded.Workspace.Id.Value),
    ];

    private static string[] TablesOf(IEnumerable<string> inserts) =>
        [.. inserts.Select(insert => InsertInto().Match(insert).Groups["table"].Value)];

    private static Exception Innermost(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current;
    }

    [GeneratedRegex("""INSERT INTO "?access"?\."?(?<table>[a-z_]+)"?""", RegexOptions.IgnoreCase)]
    private static partial Regex InsertInto();

    private async Task<FoundedCompany> FoundAsync(FoundingRequest request)
    {
        await using var module = ModuleServices.Start(database.Api);
        module.Subject.Current = request.User;
        await using var scope = module.Scope();
        return await scope.ServiceProvider.GetRequiredService<FoundingUnitOfWork>()
            .FoundCompanyAsync(request.Reference, request.OrganizationName, request.WorkspaceName, request.Founder, Token);
    }

    /// <summary>Fails if any row of the organization, or the profile of its founder, exists.</summary>
    private async Task AssertNothingOfAsync(FoundingRequest request)
    {
        var organization = request.Reference.Value;
        Assert.Equal(0L, await database.CountAsSuperuserAsync(
            "select count(*) from access.organizations where id = @organization", ("organization", organization)));
        foreach (var table in new[] { "ownerships", "memberships", "workspace_accesses", "workspaces" })
        {
            Assert.Equal(0L, await database.CountAsSuperuserAsync(
                $"select count(*) from access.{table} where organization_id = @organization", ("organization", organization)));
        }

        Assert.Equal(0L, await database.CountAsSuperuserAsync(
            "select count(*) from access.profiles where user_id = @user", ("user", request.User)));
    }

    private static async Task<IReadOnlyList<string>> SeenAsync(Session session, FoundedCompany founded)
    {
        var seen = new List<string>();
        foreach (var (table, id) in RowsOf(founded))
        {
            if (await session.CountAsync($"select count(*) from access.{table} where id = @id", ("id", id)) > 0)
            {
                seen.Add(table);
            }
        }

        return seen;
    }

    private static async Task<IReadOnlyList<(Guid Id, string Name)>> WorkspacesOfAsync(string connectionString, OrganizationId organization)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "select id, name from access.workspaces where organization_id = @organization order by id", connection);
        command.Parameters.AddWithValue("organization", organization.Value);
        await using var reader = await command.ExecuteReaderAsync(Token);
        var workspaces = new List<(Guid, string)>();
        while (await reader.ReadAsync(Token))
        {
            workspaces.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        return workspaces;
    }

    private async Task<(DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset? UpdatedAt)> AuditOfAsync(string table, Guid id)
    {
        await using var connection = new NpgsqlConnection(database.Superuser);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            $"select created_at, created_by, updated_at from access.{table} where id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token), $"No row {id} in access.{table}.");
        return (
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }
}
