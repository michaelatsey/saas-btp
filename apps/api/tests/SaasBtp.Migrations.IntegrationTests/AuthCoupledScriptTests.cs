using System.Reflection;
using System.Text.RegularExpressions;
using SaasBtp.Testing;

namespace SaasBtp.Migrations.IntegrationTests;

/// <summary>
/// The auth-coupled scripts (<c>sql.md</c>, "Auth-coupled scripts"; ADR-ARCH-038, Consequences).
/// A bare PostgreSQL has neither the <c>auth</c> schema nor the Supabase roles, and faking them would
/// prove nothing, so the hook itself is validated on the staging project. Here: the exclusion by
/// name, what an unlisted script does on bare PostgreSQL, and the text of the scripts, which stands in
/// for executing them.
/// </summary>
public sealed partial class AuthCoupledScriptTests
{
    private const string ScriptResourcePrefix = "SaasBtp.Migrations.Scripts.";

    private const string HookScript = "0004__access__create_access_token_hook.sql";

    private static readonly Assembly Migrations = Assembly.Load(new AssemblyName("SaasBtp.Migrations"));

    [Fact]
    public async Task AnAuthCoupledScriptNotListedFailsOnBarePostgres()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await RolesDatabase.StartAsync(cancellationToken);

        // The runner as a deployment runs it: no exclusion, so the hook's script is not listed.
        var result = await Runner.RunAsync(database.MigratorConnectionString, [], cancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(HookScript, result.Output, StringComparison.Ordinal);
        Assert.Contains("role \"supabase_auth_admin\" does not exist", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHookScriptIsListed()
    {
        Assert.Contains(HookScript, AuthCoupledScripts.FileNames);
    }

    [Fact]
    public void EveryListedScriptIsAScriptTheRunnerCarries()
    {
        var scripts = Migrations.GetManifestResourceNames();

        Assert.All(AuthCoupledScripts.FileNames, name => Assert.Contains(ScriptResourcePrefix + name, scripts));
    }

    [Fact]
    public void TheHookIsAPlpgsqlFunctionThatIsNotSecurityDefiner()
    {
        var script = Statements(Script(HookScript));

        var function = Assert.Single(script, statement => statement.StartsWith("create function", StringComparison.Ordinal));
        Assert.Matches(@"^create function access\.custom_access_token_hook\(event jsonb\)\s+returns jsonb\s", function);
        Assert.Matches(@"\slanguage plpgsql\s", function);
        Assert.Matches(@"\ssecurity invoker\s", function);
        Assert.DoesNotContain(script, statement => statement.Contains("security definer", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHookScriptGrantsAndRevokesWhatAdrArch038Requires()
    {
        var statements = Statements(Script(HookScript));

        Assert.Contains("grant usage on schema access to supabase_auth_admin", statements);
        Assert.Contains("grant execute on function access.custom_access_token_hook(jsonb) to supabase_auth_admin", statements);
        Assert.Contains("revoke execute on function access.custom_access_token_hook(jsonb) from public, anon, authenticated", statements);
        Assert.DoesNotContain(statements, statement => statement.StartsWith("grant", StringComparison.Ordinal)
            && !statement.EndsWith(" to supabase_auth_admin", StringComparison.Ordinal));
    }

    [Fact]
    public void NeitherAScriptNorRolesSqlGrantsAnythingOnAuth()
    {
        var files = Migrations.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => (Name: name, Text: Script(name[ScriptResourcePrefix.Length..])))
            .Append((Name: "infra/supabase/roles.sql", Text: File.ReadAllText(MetadataPath("RolesScriptPath"))))
            .ToList();

        Assert.Contains(files, file => file.Name.EndsWith(HookScript, StringComparison.Ordinal));
        foreach (var (name, text) in files)
        {
            var onAuth = Statements(text)
                .Where(statement => statement.StartsWith("grant", StringComparison.Ordinal)
                    || statement.StartsWith("alter default privileges", StringComparison.Ordinal))
                .Where(statement => AuthObject().IsMatch(statement));
            Assert.True(!onAuth.Any(), $"{name} grants on auth: {string.Join(" | ", onAuth)}");
        }
    }

    [Fact]
    public void TheLocalMigrateStepExcludesEveryListedScriptAndNamesTheList()
    {
        var guide = File.ReadAllText(MetadataPath("ApiGuidePath"));
        var migrate = Assert.Single(MigrateSection().Matches(guide)).Value;
        var command = Assert.Single(MigrateCommand().Matches(migrate)).Value;

        Assert.All(AuthCoupledScripts.FileNames, name => Assert.Contains($" {Runner.ExcludeArgument} {name}", command, StringComparison.Ordinal));
        Assert.Contains("apps/api/tests/SaasBtp.Testing/AuthCoupledScripts.cs", migrate, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStagingDeploymentRunsTheRunnerWithNoExclusion()
    {
        var workflow = File.ReadAllText(MetadataPath("DeployStagingWorkflowPath"));

        var run = Assert.Single(RunnerImageRun().Matches(workflow));
        Assert.Equal(string.Empty, run.Groups["arguments"].Value.Trim());
        Assert.DoesNotContain(Runner.ExcludeArgument, workflow, StringComparison.Ordinal);
    }

    private static string Script(string fileName)
    {
        using var stream = Migrations.GetManifestResourceStream(ScriptResourcePrefix + fileName)
            ?? throw new InvalidOperationException($"The runner carries no script {fileName}.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// The statements of a script, comments removed, lower-cased, with runs of white space made one
    /// space. A dollar-quoted body is split too: no fragment of it starts with grant.
    /// </summary>
    private static List<string> Statements(string script) =>
        LineComment().Replace(script, string.Empty)
            .Split(';')
            .Select(statement => WhiteSpace().Replace(statement, " ").Trim().ToLowerInvariant())
            .Where(statement => statement.Length > 0)
            .ToList();

    private static string MetadataPath(string key) =>
        typeof(AuthCoupledScriptTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value!;

    [GeneratedRegex("--[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    // An object of auth, or a privilege on or in the schema auth.
    [GeneratedRegex(@"\bauth\.|\b(on|in) schema auth\b")]
    private static partial Regex AuthObject();

    [GeneratedRegex(@"^## Migrate\n.*?(?=^## )", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex MigrateSection();

    [GeneratedRegex(@"^dotnet run --project apps/api/database/SaasBtp\.Migrations\b.*$", RegexOptions.Multiline)]
    private static partial Regex MigrateCommand();

    [GeneratedRegex(@"docker run [^\n]*""\$MIGRATOR_IMAGE@\$MIGRATOR_DIGEST""(?<arguments>[^\n]*)")]
    private static partial Regex RunnerImageRun();
}
