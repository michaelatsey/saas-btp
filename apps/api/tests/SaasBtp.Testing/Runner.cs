using System.Diagnostics;

namespace SaasBtp.Testing;

/// <summary>What one execution of the migration runner returned and wrote.</summary>
/// <param name="ExitCode">The runner's process exit code.</param>
/// <param name="Output">What it wrote to its standard output, then to its standard error.</param>
public sealed record RunnerResult(int ExitCode, string Output);

/// <summary>
/// Runs the migration runner the way a deployment does: as its own process, with its connection
/// string in the environment.
/// </summary>
public static class Runner
{
    /// <summary>The environment variable the runner reads its connection string from.</summary>
    public const string ConnectionStringVariable = "ConnectionStrings__Migrations";

    /// <summary>
    /// The folder, under the test's output folder, that holds the runner's own build output: its
    /// assembly, its <c>deps.json</c> and its dependencies (<c>SaasBtp.Testing.csproj</c>).
    /// </summary>
    public const string Folder = "migration-runner";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>The runner's argument that excludes one script, by its file name.</summary>
    public const string ExcludeArgument = "--exclude";

    /// <summary>
    /// Runs the runner as every Testcontainers migration does: excluding each script of
    /// <see cref="AuthCoupledScripts.FileNames"/>, which a bare PostgreSQL cannot apply.
    /// </summary>
    /// <param name="connectionString">The runner's connection string; null leaves the variable unset.</param>
    /// <param name="cancellationToken">Stops the wait; the runner is killed if it is still running.</param>
    /// <returns>The runner's exit code and output.</returns>
    public static Task<RunnerResult> RunAsync(string? connectionString, CancellationToken cancellationToken) =>
        RunAsync(connectionString, AuthCoupledScripts.FileNames.SelectMany(name => new[] { ExcludeArgument, name }).ToArray(), cancellationToken);

    /// <summary>
    /// Runs <c>SaasBtp.Migrations.dll</c> from <see cref="Folder"/>, with the runner's own
    /// dependencies rather than the test project's, with exactly the arguments given, and waits for
    /// it to exit. Only the tests of the runner's arguments, and of a script that is not excluded,
    /// call it directly.
    /// </summary>
    /// <param name="connectionString">The runner's connection string; null leaves the variable unset.</param>
    /// <param name="arguments">The runner's command-line arguments, passed as they are.</param>
    /// <param name="cancellationToken">Stops the wait; the runner is killed if it is still running.</param>
    /// <returns>The runner's exit code and output.</returns>
    public static async Task<RunnerResult> RunAsync(
        string? connectionString, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        var startInfo = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, Folder, "SaasBtp.Migrations.dll"));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Remove(ConnectionStringVariable);
        if (connectionString is not null)
        {
            startInfo.Environment[ConnectionStringVariable] = connectionString;
        }

        using var runner = Process.Start(startInfo) ?? throw new InvalidOperationException("The runner process did not start.");
        try
        {
            var standardOutput = runner.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardError = runner.StandardError.ReadToEndAsync(timeout.Token);
            await runner.WaitForExitAsync(timeout.Token);

            return new RunnerResult(runner.ExitCode, await standardOutput + await standardError);
        }
        finally
        {
            if (!runner.HasExited)
            {
                runner.Kill(entireProcessTree: true);
            }
        }
    }
}
