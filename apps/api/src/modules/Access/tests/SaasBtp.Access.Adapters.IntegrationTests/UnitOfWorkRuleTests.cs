using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Access.Adapters.Persistence;
using SaasBtp.Access.Adapters.Persistence.Founding;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The transaction rules of the Access unit of work (ADR-ARCH-033 §4, ADR-ARCH-034 §3), proven by
/// the module's integration tests as ADR-ARCH-034 §7 requires, on the context and unit of work
/// <c>AddAccessModule</c> registers.
/// </summary>
public sealed class UnitOfWorkRuleTests(AccessDatabase database)
{
    private const string OutsideATransaction = "A command was sent outside a transaction and refused before it reached the database.";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Adr033_ACommandWithoutATransactionIsRefused()
    {
        var user = Guid.NewGuid();
        await using var module = ModuleServices.Start(database.Api);
        module.Subject.Current = user;
        await using var scope = module.Scope();
        var services = scope.ServiceProvider;

        // A query outside a unit of work.
        var query = await Assert.ThrowsAnyAsync<Exception>(() => services.GetRequiredService<AccessDbContext>().Set<Profile>().CountAsync(Token));
        Assert.Contains(Chain(query), exception => exception is InvalidOperationException && exception.Message.StartsWith(OutsideATransaction, StringComparison.Ordinal));

        // A save outside a unit of work: CommitAsync flushes with no transaction open.
        var profile = new Profile(ProfileId.New(ModuleServices.Now), new UserId(user), ModuleServices.Now, new Actor(user.ToString()));
        await services.GetRequiredService<IProfileRepository>().AddAsync(profile, Token);
        var save = await Assert.ThrowsAsync<PersistenceException>(() => services.GetRequiredService<IUnitOfWork>().CommitAsync(Token).AsTask());
        Assert.Contains(Chain(save), exception => exception is InvalidOperationException && exception.Message.StartsWith(OutsideATransaction, StringComparison.Ordinal));

        Assert.Equal(0L, await database.CountAsSuperuserAsync("select count(*) from access.profiles where user_id = @user", ("user", user)));
        Assert.Empty(module.Commands.Executed);
    }

    /// <summary>
    /// "First" is shown by the first command EF Core sends in the unit of work reading the subject: the
    /// provider sets it through ADO.NET when the transaction starts, so the command log never holds it.
    /// </summary>
    [Fact]
    public async Task Adr033_TheSubjectIsSetFirst()
    {
        var user = Guid.NewGuid();
        await using (var module = ModuleServices.Start(database.Api))
        {
            module.Subject.Current = user;
            await using var scope = module.Scope();
            var sql = scope.ServiceProvider.GetRequiredService<IUnitOfWorkSqlQuery<AccessDbContext>>();

            var seen = await scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>().ExecuteAsync(
                static async (sql, ct) => (await sql.SqlQueryAsync<string>($"select current_setting('app.user_id', true) as \"Value\"", ct)).Single(),
                sql,
                Token);

            Assert.Equal(user.ToString(), seen);
            Assert.Single(module.Commands.Executed);
        }

        // Without a subject, the unit of work fails before its operation runs and before any command.
        await using (var module = ModuleServices.Start(database.Api))
        {
            module.Subject.Current = null;
            await using var scope = module.Scope();
            var ran = false;

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>().ExecuteAsync(
                    (_, _) =>
                    {
                        ran = true;
                        return Task.CompletedTask;
                    },
                    0,
                    Token));

            Assert.StartsWith("A transaction was started without a subject", refusal.Message, StringComparison.Ordinal);
            Assert.False(ran);
            Assert.Empty(module.Commands.Executed);
        }
    }

    [Fact]
    public async Task Adr034_ASqlReadSeesTheSubjectOfTheUnitOfWork()
    {
        var request = FoundingRequest.New();
        await using var module = ModuleServices.Start(database.Api);
        module.Subject.Current = request.User;

        // A founding gives the subject a profile.
        Guid profile;
        await using (var scope = module.Scope())
        {
            var founded = await scope.ServiceProvider.GetRequiredService<FoundingUnitOfWork>()
                .FoundAsync(request.Reference, request.OrganizationName, request.WorkspaceName, request.Founder, Token);
            profile = founded.Profile.Id.Value;
        }

        await using (var scope = module.Scope())
        {
            var services = scope.ServiceProvider;
            var sql = services.GetRequiredService<IUnitOfWorkSqlQuery<AccessDbContext>>();
            var unitOfWork = services.GetRequiredService<ITransactionalUnitOfWork>();

            // In a new unit of work, the read sees its subject.
            var current = await unitOfWork.ExecuteAsync(
                static async (sql, ct) => (await sql.SqlQueryAsync<Guid?>($"select access.current_profile_id() as \"Value\"", ct)).Single(),
                sql,
                Token);
            Assert.Equal(profile, current);

            // Inside one unit of work, the read sees a row the unit of work saved before it, before commit.
            var other = Guid.NewGuid();
            module.Subject.Current = other;
            var saved = new Profile(ProfileId.New(ModuleServices.Now), new UserId(other), ModuleServices.Now, new Actor(other.ToString()));
            var read = await unitOfWork.ExecuteAsync(
                static async (state, ct) =>
                {
                    await state.Profiles.AddAsync(state.Saved, ct);
                    await state.UnitOfWork.CommitAsync(ct);
                    return (await state.Sql.SqlQueryAsync<Guid?>($"select access.current_profile_id() as \"Value\"", ct)).Single();
                },
                (Profiles: services.GetRequiredService<IProfileRepository>(), Saved: saved, UnitOfWork: (IUnitOfWork)unitOfWork, Sql: sql),
                Token);
            Assert.Equal(saved.Id.Value, read);
        }
    }

    [Fact]
    public void AddAccessModuleWithoutAConnectionStringRegistersNoPersistence()
    {
        foreach (var connectionString in new[] { null, string.Empty, "  " })
        {
            var services = new ServiceCollection();

            services.AddAccessModule(ModuleServices.Configuration(connectionString));

            Assert.DoesNotContain(services, service =>
                service.ServiceType == typeof(AccessDbContext)
                || service.ServiceType == typeof(IUnitOfWork)
                || service.ServiceType == typeof(ITransactionalUnitOfWork)
                || service.ServiceType == typeof(FoundingUnitOfWork));
        }
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
