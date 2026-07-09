using MicroKit.Tenancy;
using SaasBtp.Site.Domain.Context;

namespace SaasBtp.Site.Infrastructure.Context;

/// <summary>
/// A site definition loaded from configuration (mirrors MicroKit's <c>TenantRecord</c>). Consumed by
/// <see cref="ConfigurationSiteScopeProvider"/>. Provisional Story A source; replaced by Membership
/// (Story B) with no API change.
/// </summary>
/// <remarks>
/// Both <see cref="Id"/> and <see cref="TenantId"/> bind from the nested <c>{ "Value": ... }</c>
/// shape, exactly like the tenant seed. <see cref="MicroKit.Tenancy.TenantId"/> is used only for
/// this config binding (its type flows in transitively via Site.Application); the projected
/// <see cref="SiteScope"/> flattens it to a raw <see cref="Guid"/> to keep the domain reference-free.
/// </remarks>
public sealed record SiteRecord
{
    /// <summary>The site identifier (nested <c>{ "Value": ... }</c> config shape).</summary>
    public required SiteId Id { get; init; }

    /// <summary>The tenant the site belongs to (nested <c>{ "Value": ... }</c> config shape).</summary>
    public required TenantId TenantId { get; init; }

    /// <summary>Human-readable site (chantier) name.</summary>
    public required string Name { get; init; }

    /// <summary>Projects this configuration record to the domain <see cref="SiteScope"/>.</summary>
    /// <returns>The equivalent <see cref="SiteScope"/> (tenant flattened to its <see cref="Guid"/>).</returns>
    public SiteScope ToScope() => new(Id, TenantId.Value, Name);
}
