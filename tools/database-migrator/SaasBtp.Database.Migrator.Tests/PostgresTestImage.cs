namespace SaasBtp.Database.Migrator.Tests;

/// <summary>
/// The PostgreSQL container image the anti-drift tests spin up. Kept in step with
/// SaasBtp.Safety.IntegrationTests' copy so both harnesses assert against the same Postgres major.
/// </summary>
/// <remarks>
/// Defaults to the Postgres major matching the target Supabase project (currently 17); override with
/// the <c>SAASBTP_TEST_PG_IMAGE</c> environment variable to pin another major/tag.
/// </remarks>
internal static class PostgresTestImage
{
    /// <summary>The image tag: env <c>SAASBTP_TEST_PG_IMAGE</c>, else <c>postgres:17-alpine</c>.</summary>
    public static string Name { get; } =
        Environment.GetEnvironmentVariable("SAASBTP_TEST_PG_IMAGE") ?? "postgres:17-alpine";
}
