using System.Reflection;

namespace SaasBtp.Database.Migrator.Tests;

/// <summary>
/// Pure (no-database, no-Docker) guards on <see cref="MigrationRunner"/>'s portable / auth-coupled
/// split — the single source of truth that decides which scripts a bare-PostgreSQL harness may run
/// (ADR-ARCH-006, conventions/sql.md §Auth-coupled scripts).
/// </summary>
/// <remarks>
/// After the identity-model rebaseline, the only auth-coupled script is
/// <c>0003_access_identity_auth</c>: it names the <c>supabase_auth_admin</c> role (grant, SELECT
/// policy, and the JWT hook it executes), none of which exists on bare Postgres, so it MUST be
/// excluded from the portable subset the anti-drift schema tests apply. An auth-coupled script left
/// off <see cref="MigrationRunner.AuthCoupledScriptMarkers"/> would run on bare Postgres and fail
/// there; these tests make forgetting the marker fail HERE instead — cheaply, and without Docker
/// (they run even where the Testcontainer harness cannot).
/// </remarks>
public sealed class MigrationScriptClassificationTests
{
    private static readonly Assembly MigratorAssembly = typeof(MigrationRunner).Assembly;

    [Fact]
    public void AuthCoupledMarkers_Include_0003_IdentityAuth()
    {
        // 0003 names supabase_auth_admin (grant + policy) and defines the hook, so it belongs on the
        // auth-coupled list — while its portable sibling 0002 must NOT (asserted below).
        MigrationRunner.AuthCoupledScriptMarkers
            .ShouldContain("0003_access_identity_auth");
    }

    [Theory]
    [InlineData("0003_access_identity_auth")]
    public void AuthCoupledScript_IsExcluded_FromPortableRun(string marker)
    {
        var resourceName = ResolveEmbeddedScript(marker);

        // Bound to the REAL embedded-resource name (below), so this asserts what actually ships, not a
        // hand-typed guess. auth-coupled => NOT portable => excluded from the bare-Postgres run.
        MigrationRunner.IsPortableScript(resourceName).ShouldBeFalse();
    }

    [Theory]
    [InlineData("0001_safety_create_constats")]
    [InlineData("0002_access_identity_model")]
    [InlineData("0004_site_model")]
    public void PortableScript_IsIncluded_InPortableRun(string marker)
    {
        var resourceName = ResolveEmbeddedScript(marker);

        // Portable scripts name no GoTrue object, so they run on bare Postgres. 0002 (profiles + tenants
        // + memberships) and 0004 (site.sites + site.site_memberships) are portable BY DESIGN so their
        // schemas are anti-drift tested; the only auth-coupled half is 0003. A portable script must not be
        // dragged off the list.
        MigrationRunner.IsPortableScript(resourceName).ShouldBeTrue();
    }

    /// <summary>
    /// Resolves the single embedded <c>.sql</c> resource whose name contains <paramref name="marker"/>,
    /// so the classification assertions run against what the assembly actually embeds. A script that is
    /// not embedded (e.g. a renamed file the marker no longer matches) fails loudly right here.
    /// </summary>
    private static string ResolveEmbeddedScript(string marker)
    {
        var matches = MigratorAssembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                           && name.Contains(marker, StringComparison.Ordinal))
            .ToList();

        matches.Count.ShouldBe(1, $"exactly one embedded .sql resource should contain '{marker}'");
        return matches[0];
    }
}
