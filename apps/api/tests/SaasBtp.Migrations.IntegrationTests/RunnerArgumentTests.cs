using SaasBtp.Testing;

namespace SaasBtp.Migrations.IntegrationTests;

/// <summary>
/// The runner's command-line arguments are checked before it connects: the connection string
/// points to a closed port, so a run that went as far as connecting would fail on the connection
/// instead, and say so.
/// </summary>
public sealed class RunnerArgumentTests
{
    private static readonly string UnreachableConnectionString =
        $"Host=127.0.0.1;Port={RunnerFailureTests.ClosedPort()};Database=postgres;Username=postgres;Password=unused";

    [Fact]
    public async Task AnExclusionThatNamesNoScriptFailsTheRunnerBeforeItConnects()
    {
        var result = await RunAsync([Runner.ExcludeArgument, "9999__access__not_a_script.sql"]);

        AssertRefusedBeforeConnecting(result, "--exclude names no script of the runner: 9999__access__not_a_script.sql");
    }

    [Fact]
    public async Task AnExclusionThatIsOnlyTheEndOfAScriptNameFailsTheRunner()
    {
        var result = await RunAsync([Runner.ExcludeArgument, "access_token_hook.sql"]);

        AssertRefusedBeforeConnecting(result, "--exclude names no script of the runner: access_token_hook.sql");
    }

    [Fact]
    public async Task AnExclusionThatDiffersInCaseFromAScriptNameFailsTheRunner()
    {
        var result = await RunAsync([Runner.ExcludeArgument, AuthCoupledScripts.FileNames[0].ToUpperInvariant()]);

        AssertRefusedBeforeConnecting(result, "--exclude names no script of the runner:");
    }

    [Fact]
    public async Task AnExclusionWithoutAScriptNameFailsTheRunner()
    {
        var result = await RunAsync([Runner.ExcludeArgument]);

        AssertRefusedBeforeConnecting(result, "--exclude needs the file name of a script");
    }

    [Fact]
    public async Task AnUnknownArgumentFailsTheRunner()
    {
        var result = await RunAsync(["--dry-run"]);

        AssertRefusedBeforeConnecting(result, "Unknown argument: --dry-run");
    }

    private static Task<RunnerResult> RunAsync(string[] arguments) =>
        Runner.RunAsync(UnreachableConnectionString, arguments, TestContext.Current.CancellationToken);

    private static void AssertRefusedBeforeConnecting(RunnerResult result, string reason)
    {
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(reason, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Migration failed", result.Output, StringComparison.Ordinal);
    }
}
