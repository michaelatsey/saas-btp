using System.Diagnostics.CodeAnalysis;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

/// <summary>
/// PostgreSQL (Npgsql) provider extension for <see cref="EfCoreBuilder"/>.
/// </summary>
public static class PostgreSqlEfCoreBuilderExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> as a scoped <see cref="DbContext"/>, with
    /// <c>UseMicroKitPersistence(<see cref="PostgreSqlFailureClassifier"/>)</c>, using the
    /// Npgsql provider with the given <paramref name="connectionString"/>, leased from a context pool
    /// when <see cref="ProviderOptions.PoolSize"/> is set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every context registered here refuses commands outside a transaction, and every transaction it
    /// begins sets its subject first (ADR-ARCH-033 §4; added in this repository, ADR-ARCH-035 §8):
    /// </para>
    /// <list type="bullet">
    ///   <item>EF Core's automatic transactions are disabled, so that <c>SaveChanges</c> never begins one;</item>
    ///   <item>each transaction, once started, first sends
    ///   <c>select set_config('app.user_id', @subject, true)</c> with the subject of
    ///   <see cref="IUnitOfWorkSubject"/>; a transaction started without one fails with an error;</item>
    ///   <item>a command sent outside a transaction is refused with an error, before it reaches the database;</item>
    ///   <item>a transaction passed with <c>Database.UseTransaction</c>, which the context did not begin and
    ///   which has no subject, is refused with an error as soon as it is used.</item>
    /// </list>
    /// <para>
    /// The unit of work runs every command in the one transaction it opened, and refuses a transaction
    /// it did not open (<c>EfUnitOfWork</c>, issue #212). <c>CommitAsync</c> and <c>TryCommitAsync</c>
    /// called outside <c>ExecuteAsync</c> flush outside a transaction, so the save is refused: they fail
    /// with a <c>PersistenceException</c> whose inner exceptions hold the refusal.
    /// </para>
    /// <para>
    /// <see cref="IUnitOfWorkSubject"/> is resolved from the scope that resolves the context, or, with
    /// <see cref="ProviderOptions.PoolSize"/> set, once from the root provider: it must then be registered as a singleton.
    /// </para>
    /// </remarks>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> type.</typeparam>
    /// <param name="builder">The EF Core builder.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="npgsqlOptions">
    ///   Optional delegate to further configure Npgsql-specific options such as command timeout or
    ///   schema name. It runs after <paramref name="options"/> is applied, so it can override it.
    /// </param>
    /// <param name="options">
    ///   Opt-in transient-failure retry and context pooling; both are off when <see langword="null"/>.
    /// </param>
    /// <returns><paramref name="builder"/> for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    ///   When the first context is resolved: <see cref="ProviderOptions.PoolSize"/> is set and
    ///   <see cref="IUnitOfWorkSubject"/> is registered with a lifetime other than singleton.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <paramref name="connectionString"/> is empty or white space; or
    ///   <see cref="ProviderOptions.PoolSize"/> is set and <typeparamref name="TContext"/>'s only public
    ///   constructor does not take just <c>DbContextOptions</c> or <c>DbContextOptions&lt;TContext&gt;</c>.
    /// </exception>
    public static EfCoreBuilder UsePostgreSQL<TContext>(
        this EfCoreBuilder builder,
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptions = null,
        ProviderOptions? options = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var retry = options?.Retry;
        void Configure(IServiceProvider services, DbContextOptionsBuilder opts) =>
            opts.UseNpgsql(connectionString, npgsql =>
                {
                    if (retry is not null)
                        npgsql.EnableRetryOnFailure(retry.MaxRetryCount, retry.MaxRetryDelay, errorCodesToAdd: null);
                    npgsqlOptions?.Invoke(npgsql);
                })
                .UseMicroKitPersistence(PostgreSqlFailureClassifier.Instance)
                .AddInterceptors(
                    NoAutomaticTransactionInterceptor.Instance,
                    new SubjectTransactionInterceptor(services.GetService<IUnitOfWorkSubject>()),
                    UsedTransactionInterceptor.Instance,
                    TransactionRequiredCommandInterceptor.Instance);

        if (options?.PoolSize is { } poolSize)
        {
            EnsurePoolable<TContext>(nameof(options));
            var registrations = builder.Services;
            builder.Services.AddDbContextPool<TContext>(
                (root, opts) =>
                {
                    EnsureSingletonSubject(registrations);
                    Configure(root, opts);
                },
                poolSize);
        }
        else
        {
            // Options per scope (AddDbContext's default): the subject port comes from the scope.
            builder.Services.AddDbContext<TContext>(Configure);
        }

        return builder;
    }

    // With PoolSize set, the pool's options are built once, from the root provider, and its
    // interceptors serve every lease: a scoped or transient subject port resolved there would be
    // one captured instance giving every lease's transactions the same subject. Read when the
    // options are first built, so that a port registered after UsePostgreSQL is checked too.
    // Added in this repository (ADR-ARCH-035 §8).
    private static void EnsureSingletonSubject(IServiceCollection services)
    {
        var port = services.LastOrDefault(d => d.ServiceType == typeof(IUnitOfWorkSubject) && !d.IsKeyedService);
        if (port is not null && port.Lifetime != ServiceLifetime.Singleton)
        {
            throw new InvalidOperationException(
                $"{nameof(IUnitOfWorkSubject)} is registered as {port.Lifetime}, and the context is pooled (PoolSize set): "
                + "a pooled context resolves the subject port once from the root provider and shares it with every lease. "
                + $"Register {nameof(IUnitOfWorkSubject)} as a singleton that reads the current unit of work at each call, "
                + "or remove PoolSize.");
        }
    }

    // With PoolSize set, refuse a context EF Core would pool unsafely. Kept private to each provider
    // package: a shared internal API would bind two separately versioned packages.
    private static void EnsurePoolable<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TContext>(
        string paramName)
        where TContext : DbContext
    {
        if (typeof(TContext).GetConstructors() is [var only]
            && only.GetParameters() is [var parameter]
            && (parameter.ParameterType == typeof(DbContextOptions)
                || parameter.ParameterType == typeof(DbContextOptions<TContext>)))
        {
            return;
        }

        throw new ArgumentException(
            $"'{typeof(TContext).Name}' cannot be pooled: with PoolSize set, its only public constructor must take "
            + $"just DbContextOptions or DbContextOptions<{typeof(TContext).Name}>. A pooled context's "
            + "constructor-injected scoped services are resolved once from the root provider and shared by every "
            + "lease, so tenant-aware contexts such as MultitenantDbContext must not be pooled. Remove PoolSize, or "
            + "give the context a single options-only constructor.",
            paramName);
    }
}
