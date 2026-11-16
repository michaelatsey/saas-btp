using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.Providers;

/// <summary>
/// The provider builders expose retry and pooling, off by default, and refuse to pool a context
/// whose constructor takes more than its options. Nothing here opens a
/// connection: the execution strategy and the pool are read off the registration.
/// </summary>
public sealed class ProviderOptionsTests
{
    private const string PostgreSql = "Host=localhost;Database=unused;Username=unused;Password=unused";

    [Theory]
    [InlineData("postgresql")]
    public void ByDefault_TheStrategyDoesNotRetry_AndContextsAreNotPooled(string provider)
    {
        using var services = Build(provider, options: null);

        Assert.False(RetriesOnFailure(services));
        Assert.False(IsPooled(services));
    }

    [Theory]
    [InlineData("postgresql")]
    public void WithRetry_TheStrategyRetriesOnFailure(string provider)
    {
        using var services = Build(provider, new ProviderOptions { Retry = new RetryOnFailureOptions(maxRetryCount: 3) });

        Assert.True(RetriesOnFailure(services));
    }

    [Theory]
    [InlineData("postgresql")]
    public void WithAPoolSize_ContextsAreReusedAcrossScopes(string provider)
    {
        using var services = Build(provider, new ProviderOptions { PoolSize = 8 });

        Assert.True(IsPooled(services));
    }

    [Theory]
    [InlineData("postgresql")]
    public void WithAPoolSize_AContextTakingOnlyUntypedOptions_IsPooled(string provider)
    {
        using var services = Build<UntypedOptionsContext>(provider, new ProviderOptions { PoolSize = 8 });

        Assert.True(IsPooled<UntypedOptionsContext>(services));
    }

    [Theory]
    [InlineData("postgresql")]
    public void WithAPoolSize_AContextWithAConstructorInjectedService_IsRejectedAtRegistration(string provider)
    {
        var services = new ServiceCollection();

        var thrown = Assert.Throws<ArgumentException>(() =>
            Register<ScopedServiceContext>(services, provider, new ProviderOptions { PoolSize = 8 }));

        Assert.Equal("options", thrown.ParamName);
        Assert.Contains(nameof(ScopedServiceContext), thrown.Message);
        Assert.Contains("resolved once from the root provider and shared by every lease", thrown.Message);
        Assert.Contains("MultitenantDbContext", thrown.Message);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ScopedServiceContext));
    }

    [Theory]
    [InlineData("postgresql")]
    public void WithAPoolSize_AContextWithTwoPublicConstructors_IsRejectedAtRegistration(string provider)
    {
        var services = new ServiceCollection();

        var thrown = Assert.Throws<ArgumentException>(() =>
            Register<TwoConstructorsContext>(services, provider, new ProviderOptions { PoolSize = 8 }));

        Assert.Contains(nameof(TwoConstructorsContext), thrown.Message);
    }

    [Theory]
    [InlineData("postgresql")]
    public void WithoutAPoolSize_AContextWithAConstructorInjectedService_IsRegistered(string provider)
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedService>();
        Register<ScopedServiceContext>(services, provider, options: null);
        using var root = services.BuildServiceProvider(validateScopes: true);

        using var scope = root.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ScopedServiceContext>();

        Assert.Same(scope.ServiceProvider.GetRequiredService<ScopedService>(), context.Service);
    }

    /// <summary>
    /// Pins the EF Core behaviour the registration guard relies on: a pooled context's
    /// constructor-injected scoped service is resolved once from the root provider, not from the
    /// request's scope, and every later lease shares it. EF Core does not reject such a context.
    /// </summary>
    [Fact]
    public void EfCorePool_ResolvesAConstructorInjectedScopedServiceOnceFromTheRootProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedService>();
        services.AddDbContextPool<ScopedServiceContext>(o => o.UseNpgsql(PostgreSql), poolSize: 8);
        using var root = services.BuildServiceProvider(validateScopes: false);

        ScopedServiceContext firstLease;
        using (var first = root.CreateScope())
        {
            firstLease = first.ServiceProvider.GetRequiredService<ScopedServiceContext>();
            Assert.NotSame(first.ServiceProvider.GetRequiredService<ScopedService>(), firstLease.Service);
        }

        using var next = root.CreateScope();
        var nextLease = next.ServiceProvider.GetRequiredService<ScopedServiceContext>();

        Assert.Same(firstLease, nextLease);
        Assert.Same(firstLease.Service, nextLease.Service);
        Assert.NotSame(next.ServiceProvider.GetRequiredService<ScopedService>(), nextLease.Service);
    }

    [Fact]
    public void InvalidOptions_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProviderOptions { PoolSize = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryOnFailureOptions(maxRetryCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryOnFailureOptions(maxRetryDelay: TimeSpan.Zero));
    }

    private static ServiceProvider Build(string provider, ProviderOptions? options) =>
        Build<PoolableContext>(provider, options);

    private static ServiceProvider Build<TContext>(string provider, ProviderOptions? options)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        Register<TContext>(services, provider, options);
        return services.BuildServiceProvider();
    }

    private static void Register<TContext>(ServiceCollection services, string provider, ProviderOptions? options)
        where TContext : DbContext =>
        services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef =>
        {
            // The SQL Server provider is not copied (ADR-ARCH-035 §2): PostgreSQL is the one provider.
            ArgumentOutOfRangeException.ThrowIfNotEqual(provider, "postgresql");
            ef.UsePostgreSQL<TContext>(PostgreSql, options: options);
        }));

    private static bool RetriesOnFailure(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PoolableContext>().Database.CreateExecutionStrategy().RetriesOnFailure;
    }

    // A pooled registration hands the same instance to the next scope once the first is disposed.
    private static bool IsPooled(ServiceProvider services) => IsPooled<PoolableContext>(services);

    private static bool IsPooled<TContext>(ServiceProvider services)
        where TContext : DbContext
    {
        TContext first;
        using (var scope = services.CreateScope())
            first = scope.ServiceProvider.GetRequiredService<TContext>();

        using var next = services.CreateScope();
        return ReferenceEquals(first, next.ServiceProvider.GetRequiredService<TContext>());
    }

    /// <summary>A context EF Core can pool: one public constructor taking only its options.</summary>
    public sealed class PoolableContext(DbContextOptions<PoolableContext> options) : DbContext(options);

    /// <summary>Poolable too: its only public constructor takes the untyped options.</summary>
    public sealed class UntypedOptionsContext(DbContextOptions options) : DbContext(options);

    /// <summary>A scoped service, standing in for Tenancy's <c>ITenantContextAccessor</c>.</summary>
    public sealed class ScopedService;

    /// <summary>A context whose constructor also takes a scoped service, as <c>MultitenantDbContext</c> does.</summary>
    public sealed class ScopedServiceContext(DbContextOptions<ScopedServiceContext> options, ScopedService service)
        : DbContext(options)
    {
        public ScopedService Service { get; } = service;
    }

    /// <summary>An options constructor plus a second public one: not the single constructor pooling requires.</summary>
    public sealed class TwoConstructorsContext : DbContext
    {
        public TwoConstructorsContext(DbContextOptions<TwoConstructorsContext> options) : base(options) { }

        public TwoConstructorsContext() { }
    }
}
