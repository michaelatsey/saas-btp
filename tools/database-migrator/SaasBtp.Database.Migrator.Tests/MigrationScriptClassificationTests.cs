using System.Reflection;

namespace SaasBtp.Database.Migrator.Tests;

/// <summary>
/// Pure (no-database, no-Docker) guards on <see cref="MigrationRunner"/>'s portable / auth-coupled
/// split — the single source of truth that decides which scripts a bare-PostgreSQL harness may run
/// (ADR-ARCH-006, conventions/sql.md §Auth-coupled scripts).
/// </summary>
/// <remarks>
/// 0006 (access provisioning fix) drops/creates triggers on <c>auth.users</c> and reasons about the
/// <c>supabase_auth_admin</c> role, neither of which exists on bare Postgres, so it MUST be classed
/// auth-coupled and MUST be excluded from the portable subset the anti-drift <see cref="ProfilesSchemaTests"/>
/// run applies. An auth-coupled script left off <see cref="MigrationRunner.AuthCoupledScriptMarkers"/>
/// would run on bare Postgres and fail there; these tests make forgetting the marker fail HERE instead —
/// cheaply, and without Docker (they run even where the Testcontainer harness cannot).
/// </remarks>
public sealed class MigrationScriptClassificationTests
{
    private static readonly Assembly MigratorAssembly = typeof(MigrationRunner).Assembly;

    [Fact]
    public void AuthCoupledMarkers_Include_0006_ProvisioningFix()
    {
        // 0006 touches auth.users / supabase_auth_admin, so it belongs on the auth-coupled list.
        MigrationRunner.AuthCoupledScriptMarkers
            .ShouldContain("0006_access_profiles_provisioning_fix");
    }

    [Theory]
    [InlineData("0003_access_profiles_permissions")]
    [InlineData("0004_access_profiles_trigger")]
    [InlineData("0005_access_profiles_jwt_hook")]
    [InlineData("0006_access_profiles_provisioning_fix")]
    public void AuthCoupledScript_IsExcluded_FromPortableRun(string marker)
    {
        var resourceName = ResolveEmbeddedScript(marker);

        // Bound to the REAL embedded-resource name (below), so this asserts what actually ships, not a
        // hand-typed guess. auth-coupled => NOT portable => excluded from the bare-Postgres run.
        MigrationRunner.IsPortableScript(resourceName).ShouldBeFalse();
    }

    [Theory]
    [InlineData("0001_safety_create_constats")]
    [InlineData("0002_access_create_profiles")]
    public void PortableScript_IsIncluded_InPortableRun(string marker)
    {
        var resourceName = ResolveEmbeddedScript(marker);

        // Portable scripts name no GoTrue object, so they run on bare Postgres — 0006 must not have
        // dragged a portable script off the list.
        MigrationRunner.IsPortableScript(resourceName).ShouldBeTrue();
    }

    /// <summary>
    /// Resolves the single embedded <c>.sql</c> resource whose name contains <paramref name="marker"/>,
    /// so the classification assertions run against what the assembly actually embeds. A script that is
    /// not embedded (e.g. 0006 forgotten from <c>scripts\*.sql</c>) fails loudly right here.
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
