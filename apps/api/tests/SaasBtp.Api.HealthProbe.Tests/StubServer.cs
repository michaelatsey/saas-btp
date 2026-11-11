using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SaasBtp.Api.HealthProbe.Tests;

/// <summary>
/// A Kestrel server on 127.0.0.1 and a free port, standing in for the API. It answers
/// <c>/health/live</c> with the given handler, <c>/redirected</c> with 200 and any other path
/// with 404, and records the Host header of the last request to <c>/health/live</c>.
/// </summary>
internal sealed class StubServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private StubServer(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    public int Port { get; }

    public string? LastLiveHost { get; private set; }

    public static async Task<StubServer> StartAsync(RequestDelegate live)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        var app = builder.Build();
        StubServer? server = null;
        app.Run(async context =>
        {
            switch (context.Request.Path.Value)
            {
                case "/health/live":
                    server!.LastLiveHost = context.Request.Host.Value;
                    await live(context);
                    break;
                case "/redirected":
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    break;
                default:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    break;
            }
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        server = new StubServer(app, new Uri(address).Port);
        return server;
    }

    public static RequestDelegate Answer(int statusCode) => context =>
    {
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    };

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
    }
}
