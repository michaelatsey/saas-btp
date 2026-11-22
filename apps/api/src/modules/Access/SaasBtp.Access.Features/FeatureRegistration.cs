using Microsoft.Extensions.DependencyInjection;
using SaasBtp.Access.Features.Organizations.Found;

namespace SaasBtp.Access.Features;

/// <summary>
/// The handlers of the Access module's slices, each registered explicitly, with no scanning
/// (ADR-ARCH-034 decision 2). Their endpoints are mapped by <see cref="FeatureEndpoint"/>: only an
/// <c>*Endpoint</c> type of Features depends on ASP.NET Core (ADR-ARCH-034 decision 7). Adding a
/// slice changes these two files and the slice's folder only.
/// </summary>
internal static class FeatureRegistration
{
    /// <summary>
    /// Registers the slices' handlers. They depend on the persistence adapter's ports, so
    /// <c>AccessModule</c> calls this only when it registers persistence.
    /// </summary>
    public static IServiceCollection AddFeatures(this IServiceCollection services)
    {
        services.AddScoped<FoundCompanyHandler>();
        return services;
    }
}
