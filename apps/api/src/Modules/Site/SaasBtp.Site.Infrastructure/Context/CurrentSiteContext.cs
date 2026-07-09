using SaasBtp.Site.Domain.Context;

namespace SaasBtp.Site.Infrastructure.Context;

/// <summary>
/// Request-scoped implementation of <see cref="ICurrentSiteContext"/>, populated once per request by
/// the CurrentSite middleware. Register as Scoped.
/// </summary>
/// <remarks>
/// Exposes the pure two-member Domain projection plus the Infrastructure-only <see cref="Resolution"/>
/// outcome. <see cref="Resolution"/> is public — read by the driving adapter (and unit tests) — but
/// intentionally NOT part of <see cref="ICurrentSiteContext"/>, so the purity of the Domain interface
/// is preserved by exclusion, not by an access modifier.
/// </remarks>
public sealed class CurrentSiteContext : ICurrentSiteContext
{
    /// <inheritdoc/>
    public SiteScope? CurrentSite { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<SiteScope> AvailableSites { get; private set; } = [];

    /// <summary>How the request's <c>X-Site-Id</c> was resolved. Not part of the Domain interface.</summary>
    public SiteResolution Resolution { get; private set; } = SiteResolution.NotRequested;

    /// <summary>Populates the request-scoped context. Called once by the CurrentSite middleware.</summary>
    /// <param name="resolution">The outcome of resolving <c>X-Site-Id</c>.</param>
    /// <param name="currentSite">The resolved active site, or <see langword="null"/>.</param>
    /// <param name="availableSites">The current tenant's site catalogue.</param>
    public void Populate(SiteResolution resolution, SiteScope? currentSite, IReadOnlyList<SiteScope> availableSites)
    {
        ArgumentNullException.ThrowIfNull(availableSites);
        Resolution = resolution;
        CurrentSite = currentSite;
        AvailableSites = availableSites;
    }
}
