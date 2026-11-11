using System.Collections;
using System.Globalization;

namespace SaasBtp.Api.HealthProbe;

/// <summary>
/// The readiness probe of the API image (ADR-ARCH-039): one GET of <c>/health/ready</c> on
/// 127.0.0.1, at the first port of <c>ASPNETCORE_HTTP_PORTS</c>, the endpoint Coolify's check on
/// staging already requested. Its result is an exit code in
/// the terms of Docker's HEALTHCHECK reference: 0 healthy, 1 unhealthy; 2 is reserved and never
/// returned.
/// </summary>
public static class ReadinessProbe
{
    /// <summary>Exit code of a healthy container.</summary>
    public const int Healthy = 0;

    /// <summary>Exit code of an unhealthy container.</summary>
    public const int Unhealthy = 1;

    /// <summary>
    /// How long the probe waits for an answer: shorter than the HEALTHCHECK timeout in the
    /// Dockerfile, so that the probe reports a slow answer itself instead of being killed.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private const string PortsVariable = "ASPNETCORE_HTTP_PORTS";

    // ADR-ARCH-039 sets the API's HTTP endpoint through ASPNETCORE_HTTP_PORTS only. Each of these
    // settings, bare or with a DOTNET_ or ASPNETCORE_ prefix, would move the endpoint elsewhere
    // under ASP.NET Core's configuration precedence; the probe refuses rather than follows them.
    // URLS moves it only when it is not empty: the host reads an empty one as unset and falls back
    // to HTTP_PORTS (GenericWebHostService.StartAsync). An empty HTTP_PORTS override, or Kestrel
    // endpoints, are refused as soon as they are present.
    private const string Urls = "URLS";
    private const string HttpPorts = "HTTP_PORTS";
    private const string KestrelEndpoints = "KESTREL__ENDPOINTS";
    private static readonly string[] Prefixes = ["", "DOTNET_", "ASPNETCORE_"];

    /// <summary>Requests the readiness endpoint and returns the exit code.</summary>
    /// <param name="environment">The process environment, as <see cref="Environment.GetEnvironmentVariables()"/> returns it.</param>
    /// <param name="error">Where the cause of an unhealthy result is written, one line.</param>
    /// <param name="timeout">How long to wait for an answer.</param>
    public static async Task<int> RunAsync(IDictionary environment, TextWriter error, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(error);

        if (FindMovingSetting(environment) is { } setting)
        {
            await error.WriteLineAsync($"{setting} is set; the API's HTTP endpoint is set through {PortsVariable} only (ADR-ARCH-039).");
            return Unhealthy;
        }

        var ports = environment[PortsVariable] as string;
        if (!TryReadFirstPort(ports, out var port))
        {
            await error.WriteLineAsync($"{PortsVariable} is '{ports}', not a list whose first entry is a port.");
            return Unhealthy;
        }

        var uri = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/health/ready");
        using var client = new HttpClient(CreateHandler()) { Timeout = timeout };
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                return Healthy;
            }

            await error.WriteLineAsync($"{uri} answered {(int)response.StatusCode}.");
            return Unhealthy;
        }
        catch (TaskCanceledException)
        {
            await error.WriteLineAsync($"{uri} did not answer within {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s.");
            return Unhealthy;
        }
        catch (HttpRequestException exception)
        {
            await error.WriteLineAsync($"{uri} could not be requested: {exception.Message}");
            return Unhealthy;
        }
    }

    /// <summary>
    /// Runs <paramref name="probe"/> and returns its exit code; an exception it does not handle
    /// still ends in <see cref="Unhealthy"/>, with its type and message on one line, never in a
    /// crash code Docker's HEALTHCHECK reference does not define.
    /// </summary>
    /// <param name="probe">The probe to run.</param>
    /// <param name="error">Where the cause of an unhealthy result is written.</param>
    public static async Task<int> RunGuardedAsync(Func<Task<int>> probe, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            return await probe();
        }
#pragma warning disable CA1031 // Any failure of the probe is an unhealthy result.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            await error.WriteLineAsync($"The probe failed: {exception.GetType().Name}: {exception.Message}");
            return Unhealthy;
        }
    }

    /// <summary>
    /// The handler the probe sends its request through: no proxy, so that a proxy variable in the
    /// container's environment never takes the request off 127.0.0.1, and no redirect, so that
    /// only a 2xx of <c>/health/ready</c> itself reads as healthy.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
    };

    private static string? FindMovingSetting(IDictionary environment)
    {
        foreach (DictionaryEntry entry in environment)
        {
            var name = (string)entry.Key;
            var value = entry.Value as string;
            // .NET's environment configuration reads "__" as the ":" separator, and a name may
            // carry ":" itself.
            var normalized = name.Replace(":", "__", StringComparison.Ordinal);
            foreach (var prefix in Prefixes)
            {
                if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var key = normalized[prefix.Length..];
                var moves = key.StartsWith(KestrelEndpoints, StringComparison.OrdinalIgnoreCase)
                    || key.Equals(HttpPorts, StringComparison.OrdinalIgnoreCase)
                    || (key.Equals(Urls, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value));
                if (moves && !name.Equals(PortsVariable, StringComparison.Ordinal))
                {
                    return name;
                }
            }
        }

        return null;
    }

    private static bool TryReadFirstPort(string? ports, out int port)
    {
        // Split as the host splits it (GenericWebHostService.ExpandPorts), so that the first port
        // read here is the first one the host listens on.
        port = 0;
        var entries = (ports ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return entries.Length > 0
            && int.TryParse(entries[0], NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port is >= 1 and <= 65535;
    }
}
