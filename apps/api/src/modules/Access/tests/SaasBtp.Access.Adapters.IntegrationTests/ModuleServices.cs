using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SaasBtp.Access.Adapters.Persistence.Founding;
using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.Workspaces;
using SaasBtp.Access.Features.Ports;

namespace SaasBtp.Access.Adapters.IntegrationTests;

/// <summary>
/// The Access module as <c>AddAccessModule</c> registers it, connected as <c>saasbtp_api</c> to the
/// migrated database, with what a host provides around it: the subject port, a clock fixed at
/// <see cref="Now"/>, and a log that records the commands EF Core executes.
/// </summary>
internal sealed class ModuleServices : IAsyncDisposable
{
    /// <summary>The instant every founding of these tests reads from its clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 15, 30, 250, TimeSpan.Zero);

    private readonly ServiceProvider _provider;

    private ModuleServices(ServiceProvider provider, TestSubject subject, CommandLog commands)
    {
        _provider = provider;
        Subject = subject;
        Commands = commands;
    }

    /// <summary>The subject the host would read from the validated token.</summary>
    public TestSubject Subject { get; }

    /// <summary>The commands EF Core executed, in order.</summary>
    public CommandLog Commands { get; }

    /// <summary>
    /// Registers the module on <paramref name="connectionString"/>, then lets
    /// <paramref name="configure"/> add to this provider only, such as a test interceptor.
    /// </summary>
    public static ModuleServices Start(string connectionString, Action<IServiceCollection>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var subject = new TestSubject();
        var commands = new CommandLog();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(commands));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddSingleton<IUnitOfWorkSubject>(subject);
        services.AddAccessModule(Configuration(connectionString));
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        return new ModuleServices(provider, subject, commands);
    }

    /// <summary>A configuration holding only the connection string the module reads.</summary>
    public static IConfiguration Configuration(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SaasBtp"] = connectionString })
            .Build();

    public AsyncServiceScope Scope() => _provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}

/// <summary>The subject port, set by the test as the host sets it from the token.</summary>
internal sealed class TestSubject : IUnitOfWorkSubject
{
    public Guid? Current { get; set; }
}

/// <summary>Records the text of every command EF Core reports as executed.</summary>
internal sealed class CommandLog : ILoggerProvider
{
    private const string Category = "Microsoft.EntityFrameworkCore.Database.Command";

    private readonly Lock _gate = new();

    private readonly List<string> _executed = [];

    /// <summary>The texts of the commands executed so far, in order.</summary>
    public IReadOnlyList<string> Executed
    {
        get
        {
            lock (_gate)
            {
                return [.. _executed];
            }
        }
    }

    /// <summary>The commands executed so far that insert a row.</summary>
    public IReadOnlyList<string> Inserts =>
        [.. Executed.Where(command => command.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))];

    public ILogger CreateLogger(string categoryName) =>
        categoryName == Category ? new Logger(this) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose()
    {
    }

    private void Record(string commandText)
    {
        lock (_gate)
        {
            _executed.Add(commandText);
        }
    }

    private sealed class Logger(CommandLog log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != RelationalEventId.CommandExecuted.Id || state is not IEnumerable<KeyValuePair<string, object?>> values)
            {
                return;
            }

            if (values.FirstOrDefault(value => value.Key == "commandText").Value is string commandText)
            {
                log.Record(commandText);
            }
        }
    }
}

/// <summary>
/// Fails the commit of every transaction, after the unit of work's last save and before
/// <c>COMMIT</c> is sent: the failure forced before commit of BP001-AC-17.
/// </summary>
internal sealed class FailingCommit : DbTransactionInterceptor
{
    public override InterceptionResult TransactionCommitting(
        System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) =>
        throw new ForcedFailureException();

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default) =>
        throw new ForcedFailureException();
}

/// <summary>The failure <see cref="FailingCommit"/> forces.</summary>
internal sealed class ForcedFailureException() : Exception("A failure forced before commit by the test.");

/// <summary>The inputs of one founding by a person who has no profile yet.</summary>
internal sealed record FoundingRequest(Guid User, OrganizationId Reference, OrganizationName OrganizationName, WorkspaceName WorkspaceName)
{
    public UserId Founder => new(User);

    public static FoundingRequest New()
    {
        var name = $"Company {Guid.NewGuid():N}";
        return new FoundingRequest(
            Guid.NewGuid(),
            OrganizationId.New(ModuleServices.Now),
            OrganizationName.TryCreate(name, out var organizationName, out _) ? organizationName : throw new InvalidOperationException(name),
            WorkspaceName.TryCreate(name, out var workspaceName, out _) ? workspaceName : throw new InvalidOperationException(name));
    }
}

/// <summary>The founding's unit of work, for the tests that expect it to found.</summary>
internal static class FoundingUnitOfWorkExtensions
{
    /// <summary>Founds through <see cref="FoundingUnitOfWork.FoundAsync"/>, and fails unless the company is founded.</summary>
    public static async Task<FoundedCompany> FoundCompanyAsync(
        this FoundingUnitOfWork founding,
        OrganizationId reference,
        OrganizationName organizationName,
        WorkspaceName workspaceName,
        UserId founder,
        CancellationToken cancellationToken) =>
        Assert.IsType<FoundingAttempt.Founded>(
            await founding.FoundAsync(reference, organizationName, workspaceName, founder, cancellationToken)).Company;
}
