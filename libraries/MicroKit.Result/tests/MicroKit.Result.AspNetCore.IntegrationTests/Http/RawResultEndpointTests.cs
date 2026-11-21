using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.AspNetCore.IntegrationTests.Http;

// #195 item 6: a Result<T> returned as is by an endpoint, with no converter registered (no ConfigureHttpJsonOptions),
// is written as the documented envelope, never by reflection over its properties, and never with metadata.
public sealed class RawResultEndpointTests
{
    private sealed record OrderDto(int Id, string Name);

    [Fact]
    public async Task GenericResult_WhenSuccessIsReturnedAsIs_IsSerializedAsTheEnvelope()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/order", () => Result.Success(new OrderDto(7, "Pen"))));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/order", UriKind.Relative), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(["isSuccess", "value"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.True(json.RootElement.GetProperty("isSuccess").GetBoolean());
        var value = json.RootElement.GetProperty("value");
        Assert.Equal(7, value.GetProperty("id").GetInt32());
        Assert.Equal("Pen", value.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GenericResult_WhenFailureIsReturnedAsIs_IsSerializedAsTheEnvelopeWithoutMetadata()
    {
        // Arrange
        var error = new OrderNotFoundWithMetadataError(7);
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/order", () => Result.Failure<OrderDto>(error)));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/order", UriKind.Relative), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: a plain value, so 200; ToHttpResult is what maps a failure to problem details
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("metadata", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal-host-42", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(["isSuccess", "error"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.False(json.RootElement.GetProperty("isSuccess").GetBoolean());
        var written = json.RootElement.GetProperty("error");
        Assert.Equal("ORDER.NOT_FOUND", written.GetProperty("code").GetString());
        Assert.Equal("Order 7 does not exist.", written.GetProperty("message").GetString());
        Assert.Equal("NotFound", written.GetProperty("category").GetString());
    }

    [Fact]
    public async Task GenericResult_WhenTechnicalFailureIsReturnedAsIs_BodyNeverCarriesItsMessage()
    {
        // Arrange: the envelope written without a registered converter is what a client reads
        const string KnownText = "Npgsql: connection to 10.0.0.12:5432 refused";
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/order", () => Result.Failure<OrderDto>(Error.Technical(ErrorCode.From("DB.FAIL"), KnownText))));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/order", UriKind.Relative), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(KnownText, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        var written = json.RootElement.GetProperty("error");
        Assert.Equal("DB.FAIL", written.GetProperty("code").GetString());
        Assert.Equal("Technical", written.GetProperty("category").GetString());
        Assert.False(string.IsNullOrWhiteSpace(written.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task NonGenericResult_WhenTechnicalFailureIsReturnedAsIs_BodyNeverCarriesItsMessage()
    {
        // Arrange
        const string KnownText = "Npgsql: connection to 10.0.0.12:5432 refused";
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(Error.Technical(ErrorCode.From("DB.FAIL"), KnownText))));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), content: null, cancellationToken: TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(KnownText, body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("DB.FAIL", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GenericResult_WhenHostRegistersTheFactoryAndTechnicalFailureIsReturnedAsIs_BodyNeverCarriesItsMessage()
    {
        // Arrange: the registration the usage guide shows for Minimal APIs
        const string KnownText = "Npgsql: connection to 10.0.0.12:5432 refused";
        await using var host = await ResultHttpHost.StartAsync(
            app => app.MapGet("/order", () => Result.Failure<OrderDto>(Error.Technical(ErrorCode.From("DB.FAIL"), KnownText))),
            services => services.ConfigureHttpJsonOptions(o =>
                o.SerializerOptions.Converters.Add(new MicroKit.Result.Serialization.ResultJsonConverterFactory())));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/order", UriKind.Relative), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(KnownText, body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record OrderNotFoundWithMetadataError(int OrderId)
        : Error(ErrorCode.From("ORDER.NOT_FOUND"), $"Order {OrderId} does not exist.")
    {
        public override ErrorCategory Category => ErrorCategory.NotFound;

        public override IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Create("host", "internal-host-42");
    }
}
