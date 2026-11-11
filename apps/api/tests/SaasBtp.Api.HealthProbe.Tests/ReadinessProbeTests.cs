using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace SaasBtp.Api.HealthProbe.Tests;

/// <summary>
/// The probe against a stub server: the exit code is the one Docker's HEALTHCHECK reads, 0
/// healthy and 1 unhealthy (ADR-ARCH-039).
/// </summary>
public sealed class ReadinessProbeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    [Fact]
    public async Task ReturnsHealthyWhenReadyAnswers200On127001()
    {
        await using var server = await StubServer.StartAsync(StubServer.Answer(StatusCodes.Status200OK));

        var (code, error) = await RunAsync(Ports(server.Port));

        Assert.Equal(ReadinessProbe.Healthy, code);
        Assert.Equal($"127.0.0.1:{server.Port}", server.LastReadyHost);
        Assert.Equal("", error);
    }

    [Fact]
    public async Task ReturnsUnhealthyWhenReadyAnswers503()
    {
        await using var server = await StubServer.StartAsync(StubServer.Answer(StatusCodes.Status503ServiceUnavailable));

        var (code, error) = await RunAsync(Ports(server.Port));

        Assert.Equal(ReadinessProbe.Unhealthy, code);
        Assert.Contains("503", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsUnhealthyOnARedirectAndDoesNotFollowIt()
    {
        // The target of the redirect answers 200: following it would report healthy.
        await using var server = await StubServer.StartAsync(context =>
        {
            context.Response.Redirect("/redirected");
            return Task.CompletedTask;
        });

        var (code, error) = await RunAsync(Ports(server.Port));

        Assert.Equal(ReadinessProbe.Unhealthy, code);
        Assert.Contains("302", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsUnhealthyWhenNothingListensOnThePort()
    {
        var (code, error) = await RunAsync(Ports(ClosedPort()));

        Assert.Equal(ReadinessProbe.Unhealthy, code);
        Assert.NotEqual("", error);
    }

    [Fact]
    public async Task ReturnsUnhealthyWithinItsTimeoutWhenTheServerDoesNotAnswer()
    {
        await using var server = await StubServer.StartAsync(async context =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // The probe gave up, which is what is being tested.
            }
        });
        var stopwatch = Stopwatch.StartNew();

        var (code, error) = await RunAsync(Ports(server.Port), TimeSpan.FromMilliseconds(200));

        Assert.Equal(ReadinessProbe.Unhealthy, code);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"The probe took {stopwatch.Elapsed}.");
        Assert.NotEqual("", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData(";")]
    [InlineData(" ; ")]
    public async Task ReturnsUnhealthyWhenAspNetCoreHttpPortsIsNotAPort(string? ports)
    {
        var environment = new Dictionary<string, string>();
        if (ports is not null)
        {
            environment["ASPNETCORE_HTTP_PORTS"] = ports;
        }

        var (code, error) = await RunAsync(environment);

        Assert.Equal(ReadinessProbe.Unhealthy, code);
        Assert.Contains("ASPNETCORE_HTTP_PORTS", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbesTheFirstPortOfTheList()
    {
        await using var server = await StubServer.StartAsync(StubServer.Answer(StatusCodes.Status200OK));

        var (code, _) = await RunAsync(Ports($"{server.Port};{ClosedPort()}"));

        Assert.Equal(ReadinessProbe.Healthy, code);
    }

    // The host splits HTTP_PORTS with TrimEntries and RemoveEmptyEntries (ASP.NET Core,
    // GenericWebHostService.ExpandPorts): an empty or padded entry does not change its first port.
    [Fact]
    public async Task ProbesTheFirstPortTheHostReadsFromAPaddedList()
    {
        await using var server = await StubServer.StartAsync(StubServer.Answer(StatusCodes.Status200OK));

        var (code, _) = await RunAsync(Ports($" ; {server.Port} ;{ClosedPort()}"));

        Assert.Equal(ReadinessProbe.Healthy, code);
    }

    // Each of these would make the host listen elsewhere than ASPNETCORE_HTTP_PORTS, or
    // somewhere the probe does not follow: the probe refuses rather than guesses, even though
    // the stub answers 200 on ASPNETCORE_HTTP_PORTS.
    [Theory]
    [InlineData("ASPNETCORE_URLS")]
    [InlineData("aspnetcore_urls")]
    [InlineData("DOTNET_URLS")]
    [InlineData("URLS")]
    [InlineData("DOTNET_HTTP_PORTS")]
    [InlineData("HTTP_PORTS")]
    [InlineData("Kestrel__Endpoints__Http__Url")]
    [InlineData("ASPNETCORE_Kestrel__Endpoints__Http__Url")]
    [InlineData("DOTNET_KESTREL__ENDPOINTS__HTTP__URL")]
    [InlineData("Kestrel:Endpoints:Http:Url")]
    [InlineData("ASPNETCORE_Kestrel:Endpoints:Http:Url")]
    public async Task ReturnsUnhealthyNamingASettingThatWouldMoveTheEndpoint(string variable)
    {
        await using var server = await StubServer.StartAsync(StubServer.Answer(StatusCodes.Status200OK));
        var environment = Ports(server.Port);
        environment[variable] = $"http://+:{server.Port}";

        var (code, error) = await RunAsync(environment);

        Assert.Equal(ReadinessProbe.Unhealthy, code);
        Assert.Contains(variable, error, StringComparison.Ordinal);
        Assert.Null(server.LastReadyHost);
    }

    // An empty URLS is read as unset: the host falls back to HTTP_PORTS (ASP.NET Core,
    // GenericWebHostService.StartAsync, string.IsNullOrEmpty(urls)).
    [Theory]
    [InlineData("ASPNETCORE_URLS")]
    [InlineData("DOTNET_URLS")]
    [InlineData("URLS")]
    public async Task AcceptsAnEmptyUrlsSetting(string variable)
    {
        await using var server = await StubServer.StartAsync(StubServer.Answer(StatusCodes.Status200OK));
        var environment = Ports(server.Port);
        environment[variable] = "";

        var (code, error) = await RunAsync(environment);

        Assert.Equal(ReadinessProbe.Healthy, code);
        Assert.Equal("", error);
    }

    // An empty DOTNET_HTTP_PORTS, unlike an empty URLS, replaces ASPNETCORE_HTTP_PORTS with no
    // port at all: the host then binds its default address.
    [Theory]
    [InlineData("DOTNET_HTTP_PORTS")]
    [InlineData("HTTP_PORTS")]
    public async Task RefusesAnEmptyHttpPortsOverride(string variable)
    {
        await using var server = await StubServer.StartAsync(StubServer.Answer(StatusCodes.Status200OK));
        var environment = Ports(server.Port);
        environment[variable] = "";

        var (code, error) = await RunAsync(environment);

        Assert.Equal(ReadinessProbe.Unhealthy, code);
        Assert.Contains(variable, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IgnoresTheHttpsPortsAndUnrelatedVariables()
    {
        await using var server = await StubServer.StartAsync(StubServer.Answer(StatusCodes.Status200OK));
        var environment = Ports(server.Port);
        environment["ASPNETCORE_HTTPS_PORTS"] = "8443";
        environment["ConnectionStrings__SaasBtp"] = "Host=db";
        environment["PATH"] = "/usr/bin";

        var (code, _) = await RunAsync(environment);

        Assert.Equal(ReadinessProbe.Healthy, code);
    }

    [Fact]
    public async Task AnUnexpectedExceptionEndsInUnhealthyWithItsCause()
    {
        using var error = new StringWriter();

        var code = await ReadinessProbe.RunGuardedAsync(() => throw new InvalidOperationException("boom"), error);

        Assert.Equal(ReadinessProbe.Unhealthy, code);
        Assert.Contains("InvalidOperationException: boom", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheGuardPassesTheProbeExitCodeThrough()
    {
        using var error = new StringWriter();

        Assert.Equal(ReadinessProbe.Healthy, await ReadinessProbe.RunGuardedAsync(() => Task.FromResult(ReadinessProbe.Healthy), error));
        Assert.Equal(ReadinessProbe.Unhealthy, await ReadinessProbe.RunGuardedAsync(() => Task.FromResult(ReadinessProbe.Unhealthy), error));
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void TheHandlerUsesNoProxyAndFollowsNoRedirect()
    {
        using var handler = ReadinessProbe.CreateHandler();

        Assert.False(handler.UseProxy);
        Assert.False(handler.AllowAutoRedirect);
    }

    private static Dictionary<string, string> Ports(int port) => Ports(port.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static Dictionary<string, string> Ports(string ports) => new() { ["ASPNETCORE_HTTP_PORTS"] = ports };

    private static async Task<(int Code, string Error)> RunAsync(IDictionary environment, TimeSpan? timeout = null)
    {
        using var error = new StringWriter();
        var code = await ReadinessProbe.RunAsync(environment, error, timeout ?? Timeout);
        return (code, error.ToString());
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
