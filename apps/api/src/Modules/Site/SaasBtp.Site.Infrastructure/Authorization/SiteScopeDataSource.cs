using Npgsql;

namespace SaasBtp.Site.Infrastructure.Authorization;

/// <summary>
/// The Site module's owner-role <see cref="NpgsqlDataSource"/>, wrapped in a Site-specific type so it is
/// NEVER registered as a bare, ambient <see cref="NpgsqlDataSource"/> singleton (which would become a
/// last-wins global shared across modules). Only <see cref="SiteMembershipScopeProvider"/> depends on it.
/// </summary>
/// <remarks>
/// The connection MUST use the privileged owner role (ADR-ARCH-005 / sql.md §RLS): it bypasses RLS,
/// which is required because <c>site.sites</c> and <c>site.site_memberships</c> have RLS enabled with no
/// policy (#50) — a non-bypassing role would read ZERO rows silently.
/// </remarks>
public sealed class SiteScopeDataSource(NpgsqlDataSource source) : IAsyncDisposable
{
    /// <summary>The underlying owner-role data source used by the scope provider.</summary>
    public NpgsqlDataSource Source { get; } = source;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => Source.DisposeAsync();
}
