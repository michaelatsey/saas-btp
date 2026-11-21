using System.Collections;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests;

/// <summary>A command the Npgsql driver executed: what reached the database.</summary>
/// <param name="ConnectorId">The physical connection it ran on.</param>
/// <param name="CommandText">Its text, as Npgsql sent it: named parameters become positional (<c>$1</c>).</param>
/// <param name="Parameters">Its parameter values, as Npgsql logs them.</param>
public sealed record SentCommand(int? ConnectorId, string CommandText, IReadOnlyList<string> Parameters);

/// <summary>
/// What one test observes: the commands Npgsql sent and the transactions EF Core began, recorded
/// for the test that is running. One logger factory and one data-source configuration serve every
/// test, so that EF Core sees the same options each time; each entry goes to the probe of the
/// asynchronous flow it was produced in.
/// </summary>
public sealed class Probe
{
    private static readonly AsyncLocal<Probe?> CurrentProbe = new();

    private readonly List<SentCommand> _commands = [];

    private Probe()
    {
    }

    // One instance for every registration: EF Core sees the same Npgsql options each time.
    // Declared before NpgsqlOptions, which reads it: static initializers run in textual order.
    private static readonly Action<Npgsql.NpgsqlDataSourceBuilder> DataSource =
        builder => builder.UseLoggerFactory(LoggerFactory.Instance).EnableParameterLogging();

    /// <summary>
    /// The Npgsql options every test registers its context with: the driver logs each command it
    /// executes, with its parameters, through <see cref="LoggerFactory"/>.
    /// </summary>
    public static Action<NpgsqlDbContextOptionsBuilder> NpgsqlOptions { get; } =
        npgsql => npgsql.ConfigureDataSource(DataSource);

    /// <summary>Gets the commands sent since <see cref="Start"/>, in order.</summary>
    public IReadOnlyList<SentCommand> Commands
    {
        get
        {
            lock (_commands)
                return [.. _commands];
        }
    }

    /// <summary>Gets the number of transactions EF Core began since <see cref="Start"/>.</summary>
    public int TransactionsBegun { get; private set; }

    /// <summary>Starts recording for the calling test.</summary>
    public static Probe Start()
    {
        var probe = new Probe();
        CurrentProbe.Value = probe;
        return probe;
    }

    private static void Record(SentCommand command)
    {
        if (CurrentProbe.Value is { } probe)
        {
            lock (probe._commands)
                probe._commands.Add(command);
        }
    }

    /// <summary>Counts the transactions EF Core begins; added by <see cref="SubjectDbContext"/>.</summary>
    public sealed class TransactionCounter : DbTransactionInterceptor
    {
        public static TransactionCounter Instance { get; } = new();

        public override InterceptionResult<DbTransaction> TransactionStarting(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        {
            Count();
            return result;
        }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Count();
            return ValueTask.FromResult(result);
        }

        private static void Count()
        {
            if (CurrentProbe.Value is { } probe)
                probe.TransactionsBegun++;
        }
    }

    private sealed class LoggerFactory : ILoggerFactory
    {
        public static LoggerFactory Instance { get; } = new();

        public ILogger CreateLogger(string categoryName) =>
            categoryName == "Npgsql.Command" ? CommandLogger.Instance : NullLogger.Instance;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class CommandLogger : ILogger
    {
        public static CommandLogger Instance { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Npgsql logs each command as it starts executing (ExecutingCommand,
            // ExecutingCommandWithParameters) and again once it completes; the first is the one
            // that says the command was sent.
            if (eventId.Name?.StartsWith("ExecutingCommand", StringComparison.Ordinal) != true
                || state is not IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                return;
            }

            string? text = null;
            int? connector = null;
            List<string> parameters = [];
            foreach (var (key, value) in values)
            {
                switch (key)
                {
                    case "CommandText":
                        text = value as string;
                        break;
                    case "ConnectorId":
                        connector = value as int?;
                        break;
                    case "Parameters" when value is IEnumerable list && value is not string:
                        foreach (var item in list)
                            parameters.Add(Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                        break;
                }
            }

            if (text is not null)
                Record(new SentCommand(connector, text, parameters));
        }
    }

    private sealed class NullLogger : ILogger
    {
        public static NullLogger Instance { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
