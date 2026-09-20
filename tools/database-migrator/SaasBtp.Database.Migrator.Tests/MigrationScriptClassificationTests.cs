using System.Reflection;

namespace SaasBtp.Database.Migrator.Tests;

/// <summary>
/// Pure (no-database, no-Docker) guards on <see cref="MigrationRunner"/>'s portable / auth-coupled
/// split — the single source of truth that decides which scripts a bare-PostgreSQL harness may run
/// (ADR-ARCH-006, conventions/sql.md §Auth-coupled scripts).
/// </summary>
/// <remarks>
/// BP-001 clean-slate baseline: the abandoned identity model's scripts (0001-0005) were removed and
/// replaced by a single, fully PORTABLE founding script (<c>0001_access_founding_model</c>). No script
/// names a GoTrue object, so <see cref="MigrationRunner.AuthCoupledScriptMarkers"/> is EMPTY and every
/// embedded script runs on bare Postgres. These tests run even where the Testcontainer harness cannot,
/// so a forgotten auth-coupled marker (the day such a script returns) fails HERE — cheaply, without Docker.
/// </remarks>
public sealed class MigrationScriptClassificationTests
{
    private static readonly Assembly MigratorAssembly = typeof(MigrationRunner).Assembly;

    [Fact]
    public void AuthCoupledMarkers_AreEmpty_OnTheBp001Baseline()
    {
        // No current script touches auth.users / supabase_auth_admin — the founding model is portable.
        MigrationRunner.AuthCoupledScriptMarkers.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("0001_access_founding_model")]
    public void FoundingScript_IsEmbedded_AndPortable(string marker)
    {
        var resourceName = ResolveEmbeddedScript(marker);

        // Bound to the REAL embedded-resource name (below), so this asserts what actually ships, not a
        // hand-typed guess. Portable => included in the bare-Postgres anti-drift run.
        MigrationRunner.IsPortableScript(resourceName).ShouldBeTrue();
    }

    [Fact]
    public void EveryEmbeddedScript_IsPortable_OnTheBp001Baseline()
    {
        var scripts = MigratorAssembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // At least one script ships (the founding model); with no auth-coupled markers, ALL are portable.
        scripts.ShouldNotBeEmpty();
        scripts.ShouldAllBe(name => MigrationRunner.IsPortableScript(name));
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
