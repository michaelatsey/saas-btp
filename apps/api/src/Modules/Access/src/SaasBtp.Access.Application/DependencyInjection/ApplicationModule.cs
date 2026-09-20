using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Access.Application.Features.FoundCompanySpace;

namespace SaasBtp.Access.Application.DependencyInjection;

/// <summary>Composition seam for the <c>SaasBtp.Access</c> Application layer.</summary>
public static class ApplicationModule
{
    /// <summary>
    /// Registers <c>SaasBtp.Access</c> application-layer services. Handlers are discovered and
    /// registered by the MicroKit MediatR pipeline (Infrastructure composition); this seam registers
    /// what the pipeline consumes but does not discover — the opt-in validators.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddAccessApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Validators are stateless — singletons, allocated once (opt-in per request, cqrs-mediatr).
        services.AddSingleton<IValidator<FoundCompanySpaceCommand>, FoundCompanySpaceCommandValidator>();

        return services;
    }
}
