using System.Diagnostics;

namespace SaasBtp.Migrations.IntegrationTests;

/// <summary>What one execution of the migration runner returned and wrote.</summary>
internal sealed record RunnerResult(int ExitCode, string Output);

/// <summary>
/// Runs the migration runner the way a deployment does: as its own process, with its connection
/// string in the environment.
/// </summary>
internal static class Runner
{
    public const string ConnectionStringVariable = "ConnectionStrings__Migrations";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

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
