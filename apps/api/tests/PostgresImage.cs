using Testcontainers.PostgreSql;

namespace SaasBtp.Testing;

/// <summary>
/// The PostgreSQL every integration test starts. Linked into each test project, so that the
/// image and the way its cluster is initialized are defined once.
/// </summary>
/// <remarks>
/// The image and the locale match the Supabase project: PostgreSQL 17.6, the ICU locale
/// provider and the <c>en-US</c> locale. <c>infra/docker/compose.dev.yml</c> repeats these
/// values; a test fails when the two differ.
/// </remarks>
internal static class PostgresImage
{
    public const string Name = "postgres:17.6-bookworm";

    public const string LocaleProvider = "icu";

    public const string Locale = "en-US";

    /// <summary>The value of <c>POSTGRES_INITDB_ARGS</c>, read by the image when it creates the cluster.</summary>
    public const string InitDbArgs = $"--locale-provider={LocaleProvider} --icu-locale={Locale}";

    public static PostgreSqlBuilder CreateBuilder() =>
        new PostgreSqlBuilder(Name).WithEnvironment("POSTGRES_INITDB_ARGS", InitDbArgs);
}
