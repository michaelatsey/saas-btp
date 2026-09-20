using MicroKit.MediatR.Handlers;
using MicroKit.Persistence.Abstractions;
using MicroKit.Result;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Access.Application.Features.FoundCompanySpace;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;
using SaasBtp.Access.Infrastructure.DependencyInjection;
using Shouldly;
using Xunit;

namespace SaasBtp.Access.Infrastructure.UnitTests;

/// <summary>
/// Composition tests: the module wires up, and every port the founding handler declares actually has
/// an adapter behind it. A missing registration is otherwise a runtime failure on the first request;
/// these tests turn it into a failing build.
/// </summary>
public sealed class CompositionTests
{
    private static IConfiguration Configured(
        string? connectionString = "Host=localhost;Database=x;Username=u;Password=p",
        string? projectUrl = "https://example.supabase.co",
        string? serviceRoleKey = "sb_secret_test") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AccessDb"] = connectionString,
                ["Supabase:ProjectUrl"] = projectUrl,
                ["Supabase:ServiceRoleKey"] = serviceRoleKey,
            })
            .Build();

    private static ServiceProvider BuildModule() =>
        new ServiceCollection().AddAccessModule(Configured()).BuildServiceProvider();

    [Fact]
    public void The_module_composes_and_builds_a_provider()
    {
        using var provider = BuildModule();

        provider.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(typeof(IOrganizationRepository))]
    [InlineData(typeof(IWorkspaceRepository))]
    [InlineData(typeof(IProfileProjection))]
    [InlineData(typeof(IIdentityProvisioner))]
    [InlineData(typeof(IUnitOfWork))]
    [InlineData(typeof(TimeProvider))]
    public void Every_port_the_founding_handler_needs_has_an_adapter(Type port)
    {
        using var provider = BuildModule();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService(port).ShouldNotBeNull();
    }

    [Fact]
    public void There_is_one_repository_port_per_aggregate_root_and_no_relation_port()
    {
        // Two aggregate roots, two repository ports (ADR-ARCH-016). Asserted on the composition so a
        // reinstated per-relation adapter cannot slip back in silently.
        var repositoryPorts = typeof(IOrganizationRepository).Assembly.GetTypes()
            .Where(type => type.IsInterface)
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToArray();

        repositoryPorts.ShouldBe(["IOrganizationRepository", "IWorkspaceRepository"], ignoreOrder: true);
    }

    [Fact]
    public void The_founding_handler_resolves_with_its_whole_dependency_graph()
    {
        using var provider = BuildModule();
        using var scope = provider.CreateScope();

        // Resolving the handler proves all its dependencies are satisfiable at once — the single
        // most valuable composition assertion for a use case this wide.
        scope.ServiceProvider
            .GetService<ICommandHandler<FoundCompanySpaceCommand, Result<FoundCompanySpaceResponse>>>()
            .ShouldNotBeNull();
    }

    [Fact]
    public void The_founding_validator_is_registered_so_the_pipeline_can_reject_bad_input()
    {
        using var provider = BuildModule();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetService<FluentValidation.IValidator<FoundCompanySpaceCommand>>()
            .ShouldNotBeNull();
    }

    [Fact]
    public void A_missing_connection_string_fails_loudly_at_composition_not_at_first_request()
        => Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddAccessModule(Configured(connectionString: null)));

    [Fact]
    public void A_missing_service_role_key_fails_loudly_at_composition()
        => Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddAccessModule(Configured(serviceRoleKey: null)));
}
