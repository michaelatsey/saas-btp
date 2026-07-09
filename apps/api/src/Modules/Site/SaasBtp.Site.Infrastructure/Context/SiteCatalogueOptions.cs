namespace SaasBtp.Site.Infrastructure.Context;

/// <summary>
/// Configuration options for the provisional site catalogue (mirrors MicroKit's
/// <c>MultitenancyOptions</c>). Bind via
/// <c>services.Configure&lt;SiteCatalogueOptions&gt;(config.GetSection(SiteCatalogueOptions.SectionKey))</c>.
/// </summary>
public sealed class SiteCatalogueOptions
{
    /// <summary>Configuration section key (<c>"SiteCatalogue"</c>).</summary>
    public const string SectionKey = "SiteCatalogue";

    /// <summary>Site definitions loaded from configuration. Consumed by <see cref="ConfigurationSiteScopeProvider"/>.</summary>
    public List<SiteRecord> Sites { get; init; } = [];
}
