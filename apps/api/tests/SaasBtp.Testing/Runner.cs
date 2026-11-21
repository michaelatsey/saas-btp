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

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Runs <c>SaasBtp.Migrations.dll</c> from the test's output folder, and waits for it to exit.
    /// </summary>
    /// <param name="connectionString">The runner's connection string; null leaves the variable unset.</param>
    /// <param name="cancellationToken">Stops the wait; the runner is killed if it is still running.</param>
    /// <returns>The runner's exit code and output.</returns>
    public static async Task<RunnerResult> RunAsync(string? connectionString, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        var startInfo = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SaasBtp.Migrations.dll"));
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
