using MediatR;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.MediatR.Events;
using MicroKit.MediatR.Extensions;
using MicroKit.Result;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Access.Application.Features.FoundCompanySpace;
using SaasBtp.Access.Infrastructure.DependencyInjection;
using Shouldly;
using Xunit;

namespace SaasBtp.Access.Infrastructure.UnitTests;

/// <summary>
/// The CQRS pipeline as it actually runs: which behaviors MicroKit executes, in what order, and what
/// a validation failure looks like coming back out of it.
/// </summary>
/// <remarks>
/// These assert the pipeline END TO END through the real <c>IMediator</c>, not the registrations in
/// isolation: MediatR runs <c>IPipelineBehavior&lt;,&gt;</c> in DI REGISTRATION order and ignores the
/// <c>Order</c> property entirely, so a registration-only test would pass while the pipeline ran
/// backwards.
/// </remarks>
public sealed class AccessPipelineTests
{
    private static IConfiguration Configured() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AccessDb"] = "Host=localhost;Database=x;Username=u;Password=p",
                ["Supabase:ProjectUrl"] = "https://example.supabase.co",
                ["Supabase:ServiceRoleKey"] = "sb_secret_test",
            })
            .Build();

    /// <summary>
    /// The module plus the two things a host supplies and a bare ServiceCollection does not:
    /// logging (LoggingBehavior injects ILogger&lt;&gt;) and a stand-in for the identity boundary, so
    /// no test ever reaches the real GoTrue Admin API.
    /// </summary>
    private static ServiceProvider BuildHost(SpyIdentityProvisioner spy)
    {
        var services = new ServiceCollection();
        services.AddAccessModule(Configured());
        services.AddLogging();
        // Last registration wins in MS DI — this replaces the typed HttpClient adapter.
        services.AddSingleton<IIdentityProvisioner>(spy);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_validation_failure_comes_back_as_a_result_failure_never_as_an_exception()
    {
        // The whole point of Result<T> in the pipeline: BehaviorBase.CreateFailureOrThrow builds a
        // Result.Failure when TResponse is Result<T>, and only throws for a bare-T response. A
        // thrown ValidationException here would mean the response type was wired wrongly.
        var spy = new SpyIdentityProvisioner();
        await using var provider = BuildHost(spy);
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator
            .SendCommandAsync<FoundCompanySpaceCommand, Result<FoundCompanySpaceResponse>>(
                new FoundCompanySpaceCommand("not-an-email", "", "", ""));

        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Validation);
        result.Error.ShouldBeOfType<MicroKit.MediatR.Behaviors.Errors.ValidationError>();
    }

    [Fact]
    public async Task Validation_short_circuits_before_the_identity_boundary_is_ever_touched()
    {
        // Order 300 runs before the handler, so a malformed intent must never reach GoTrue —
        // otherwise a typo in an e-mail would create an auth account for nobody.
        var spy = new SpyIdentityProvisioner();
        await using var provider = BuildHost(spy);
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.SendCommandAsync<FoundCompanySpaceCommand, Result<FoundCompanySpaceResponse>>(
            new FoundCompanySpaceCommand("", "", "", ""));

        spy.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Every_failed_rule_travels_in_one_failure_rather_than_only_the_first()
    {
        // MicroKit's ValidationBehavior collects every failure from every registered validator before
        // short-circuiting — no early exit. Four blank fields must produce four messages.
        var spy = new SpyIdentityProvisioner();
        await using var provider = BuildHost(spy);
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator
            .SendCommandAsync<FoundCompanySpaceCommand, Result<FoundCompanySpaceResponse>>(
                new FoundCompanySpaceCommand("", "", "", ""));

        var error = result.Error.ShouldBeOfType<MicroKit.MediatR.Behaviors.Errors.ValidationError>();
        error.Failures.Select(failure => failure.PropertyName).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public void The_pipeline_is_MicroKits_own_behaviors_in_the_prescribed_order()
    {
        // Registration order IS execution order. Asserted on the registrations themselves because
        // that is the thing that can silently regress — and asserted to come from MicroKit's
        // assembly, so a hand-rolled behavior cannot quietly reappear.
        var services = new ServiceCollection();
        services.AddAccessModule(Configured());

        var behaviors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType!)
            .ToArray();

        behaviors.Select(type => type.Name).ShouldBe(["LoggingBehavior`2", "ValidationBehavior`2"]);
        behaviors.ShouldAllBe(type => type.Assembly.GetName().Name == "MicroKit.MediatR.Behaviors");
    }

    [Fact]
    public void TransactionBehavior_stays_unregistered_because_its_dependency_cannot_be_resolved()
    {
        // TRIPWIRE, not a preference. TransactionBehavior(ITransactionalContext,
        // IDomainEventsDispatcher) resolves IDomainEventsDispatcher to MicroKit's
        // DomainEventDispatcher(IDomainEventsProvider, IDomainEventHandlerDispatcher), and
        // IDomainEventsProvider is implemented by AggregateRoot<TId> itself — never registered as a
        // service, by MicroKit or by anyone. Registering the behavior would fail on first dispatch.
        // When this test starts FAILING, MicroKit has shipped a provider and the transaction slice
        // can be reopened.
        var spy = new SpyIdentityProvisioner();
        using var provider = BuildHost(spy);
        using var scope = provider.CreateScope();

        var thrown = Should.Throw<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IDomainEventsDispatcher>());

        thrown.Message.ShouldContain("IDomainEventsProvider");
    }

    /// <summary>Stands in for the GoTrue adapter and records whether the pipeline ever reached it.</summary>
    private sealed class SpyIdentityProvisioner : IIdentityProvisioner
    {
        public int Calls { get; private set; }

        public ValueTask<Result<ProvisionedIdentity>> EstablishAsync(
            Email email, string fullName, CancellationToken ct = default)
        {
            Calls++;
            return ValueTask.FromResult(
                Result.Success(new ProvisionedIdentity(Guid.CreateVersion7(), Adopted: false)));
        }
    }
}
