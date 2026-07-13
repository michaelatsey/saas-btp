using Npgsql;
using NpgsqlTypes;
using SaasBtp.Site.Domain.Context.Ports;

namespace SaasBtp.Site.Infrastructure.Authorization;

/// <summary>
/// Membership-backed <see cref="ISiteScopeProvider"/> (ADR-ARCH-009). Answers "may this user act in the
/// scope of this site?" as a single per-request query over the TWO authorization edges, OR-ed:
/// (a) an ACTIVE, window-valid site membership on <c>(user_id, site_id)</c> — the branch that covers
///     people EXTERNAL to the site's tenant; or
/// (b) an ACTIVE tenant membership on <c>(user_id, tenant_id)</c> whose role confers cross-site scope
///     (<see cref="CrossSiteScopeRoles"/> — today <c>owner</c> only).
/// </summary>
/// <remarks>
/// Relationship-based authorization is a direct read over the source tables: the provider couples to the
/// SCHEMA (<c>site.site_memberships</c> + <c>access.memberships</c>), NOT to the Access module — the same
/// coupling PowerSync's parameter queries will have (ADR-ARCH-009). The role -> cross-site-scope matrix
/// lives HERE, in C# (<see cref="CrossSiteScopeRoles"/>), never in the database and never hardcoded in the
/// SQL; it is passed as a parameter. Reads run on the owner connection (<see cref="SiteScopeDataSource"/>),
/// which bypasses the no-policy tables' RLS.
/// </remarks>
public sealed class SiteMembershipScopeProvider(SiteScopeDataSource dataSource) : ISiteScopeProvider
{
    /// <summary>
    /// The tenant roles that confer scope over EVERY site in the tenant without a per-site assignment
    /// (branch b). Today only <c>owner</c>; a QSE-director-style cross-site role is a separate, later
    /// issue. Kept in C# (ADR-ARCH-009): widening this set changes no schema and needs no migration.
    /// </summary>
    public static readonly string[] CrossSiteScopeRoles = ["owner"];

    // Branch (a) uses now() within [valid_from, valid_until): is_active AND the current window. Branch (b)
    // is a plain active tenant membership whose role is in the C#-supplied cross-site set. @user_id is
    // reused across both EXISTS clauses (Npgsql binds a named parameter once, references it many times).
    private const string Sql =
        """
        SELECT
            EXISTS (
                SELECT 1
                FROM site.site_memberships AS m
                WHERE m.user_id = @user_id
                  AND m.site_id = @site_id
                  AND m.is_active
                  AND m.valid_from <= now()
                  AND (m.valid_until IS NULL OR now() < m.valid_until)
            )
            OR EXISTS (
                SELECT 1
                FROM access.memberships AS a
                WHERE a.user_id = @user_id
                  AND a.tenant_id = @tenant_id
                  AND a.is_active
                  AND a.role = ANY(@cross_site_roles)
            );
        """;

    /// <inheritdoc/>
    public async Task<bool> CanActInSiteScopeAsync(
        Guid userId, Guid tenantId, Guid siteId, CancellationToken ct = default)
    {
        await using var command = dataSource.Source.CreateCommand(Sql);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.Add(new NpgsqlParameter("cross_site_roles", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = CrossSiteScopeRoles,
        });

        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is true;
    }
}
