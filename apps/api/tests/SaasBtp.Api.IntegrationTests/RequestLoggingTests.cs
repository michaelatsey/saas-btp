using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace SaasBtp.Api.IntegrationTests;

/// <summary>
/// Runs the API as its own process and reads what it writes to standard output: the console
/// logger is the thing under test, and a test server does not go through it.
/// </summary>
public sealed class RequestLoggingTests
{
    private const string ListeningPrefix = "Now listening on: ";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task ARequestProducesSingleLineJsonLogEntriesCarryingTraceIdAndSpanId()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);

        using var api = StartApi();
        try
        {
            var listening = await ReadUntilAsync(api, entry => ListeningAddress(entry) is not null, timeout.Token);

            using var client = new HttpClient { BaseAddress = ListeningAddress(listening) };
            using var response = await client.GetAsync(new Uri("/api/v1/ping", UriKind.Relative), timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var requestEntry = await ReadUntilAsync(
                api, entry => ScopeValue(entry, "RequestPath") == "/api/v1/ping", timeout.Token);

            Assert.False(string.IsNullOrEmpty(ScopeValue(requestEntry, "TraceId")), "No TraceId in the log entry.");
            Assert.False(string.IsNullOrEmpty(ScopeValue(requestEntry, "SpanId")), "No SpanId in the log entry.");
            Assert.Equal(TimeSpan.Zero, requestEntry.GetProperty("Timestamp").GetDateTimeOffset().Offset);
        }
        finally
        {
            Stop(api);
        }
    }

    private static Process StartApi()
    {
        var startInfo = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SaasBtp.Api.dll"));
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add("http://127.0.0.1:0");

        // Not UTC: a timestamp without an offset then proves the logger was told to use UTC,
        // on a machine that is itself in UTC as much as on any other.
        startInfo.Environment["TZ"] = "Asia/Tokyo";

        return Process.Start(startInfo) ?? throw new InvalidOperationException("The API process did not start.");
    }

    /// <summary>
    /// Reads log lines up to the first entry <paramref name="isExpected"/> accepts. Every line
    /// read on the way must be one complete JSON object: that is what "single-line JSON" means.
    /// </summary>
    private static async Task<JsonElement> ReadUntilAsync(
        Process api, Func<JsonElement, bool> isExpected, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await api.StandardOutput.ReadLineAsync(cancellationToken)
                ?? throw new InvalidOperationException("The API process ended before the expected log entry.");

            var entry = ParseSingleLineJson(line);
            if (isExpected(entry))
            {
                return entry;
            }
        }
    }

    private static JsonElement ParseSingleLineJson(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            Assert.Fail($"A log line is not a single-line JSON object: {line}");
            throw;
        }
    }

    private static Uri? ListeningAddress(JsonElement entry)
    {
        var message = entry.GetProperty("Message").GetString();

        return message is not null && message.StartsWith(ListeningPrefix, StringComparison.Ordinal)
            ? new Uri(message[ListeningPrefix.Length..])
            : null;
    }

    private static string? ScopeValue(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty("Scopes", out var scopes))
        {
            return null;
        }

        foreach (var scope in scopes.EnumerateArray())
        {
            if (scope.ValueKind == JsonValueKind.Object && scope.TryGetProperty(name, out var value))
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static void Stop(Process api)
    {
        try
        {
            api.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process had already exited.
        }

        api.WaitForExit(Timeout);
    }
}
