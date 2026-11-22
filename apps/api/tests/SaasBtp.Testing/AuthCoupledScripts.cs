namespace SaasBtp.Testing;

/// <summary>
/// The one list of the auth-coupled scripts: the DbUp scripts that name a Supabase Auth role or
/// object, absent from a bare PostgreSQL (<c>sql.md</c>, "Auth-coupled scripts"; ADR-ARCH-019,
/// ADR-ARCH-038). Every Testcontainers migration excludes them by name (<see cref="Runner"/>), and
/// so does the local Migrate step of <c>apps/api/CLAUDE.md</c>; a deployment excludes none. They
/// are validated against a real Supabase project, never against a faked <c>auth</c> schema.
/// </summary>
/// <remarks>
/// A script that is auth-coupled and not listed here runs on bare PostgreSQL and fails the
/// migration, rather than being skipped silently.
/// </remarks>
public static class AuthCoupledScripts
{
    /// <summary>The file names of the auth-coupled scripts, as the runner's <c>--exclude</c> takes them.</summary>
    public static IReadOnlyList<string> FileNames { get; } =
    [
        // The custom access token hook that sets contact_confirmed (ADR-ARCH-038 decision 5).
        "0004__access__create_access_token_hook.sql",
    ];
}
