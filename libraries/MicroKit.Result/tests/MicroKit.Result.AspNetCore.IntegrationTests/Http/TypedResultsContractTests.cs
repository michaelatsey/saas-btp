using System.Net;
using System.Text.Json;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.AspNetCore.IntegrationTests.Http;

// #184 Phase 5: TypedResults, Created and Accepted, the standard problem type, the Timeout status, and a
// non-generic Result returned as is.
public sealed class TypedResultsContractTests
{
    private sealed record Order(int Id);

    private static CategorizedError OrderNotFound() =>
        Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), "The order does not exist.");

    [Fact]
    public async Task ToHttpResult_WhenUsedAsEndpointReturn_DescribesTheSuccessTypeToOpenApi()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/orders/{id}", (int id) => (id == 1 ? Result.Success(new Order(1)) : OrderNotFound()).ToHttpResult()));

        // Act
        var endpoint = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.Single();
        var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();

        // Assert: the success type is described; ProblemHttpResult has no fixed status, so it adds no metadata and
        // an endpoint declares its problem responses with ProducesProblem(status)
        Assert.Contains(produces, p => p.StatusCode == StatusCodes.Status200OK && p.Type == typeof(Order));
    }

    [Fact]
    public async Task ToCreatedHttpResult_WhenSuccess_Answers201WithLocationAndValue()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Success(new Order(7)).ToCreatedHttpResult(o => $"/orders/{o.Id}")));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/orders/7", response.Headers.Location!.OriginalString);
        Assert.Equal(7, json.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task ToCreatedHttpResult_WhenFailure_AnswersProblemDetails()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure<Order>(OrderNotFound()).ToCreatedHttpResult(o => $"/orders/{o.Id}")));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task ToAcceptedHttpResult_WhenSuccess_Answers202()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
        {
            app.MapPost("/jobs", () => Result.Success(new Order(3)).ToAcceptedHttpResult(o => $"/jobs/{o.Id}"));
            app.MapPost("/commands", () => Result.Success().ToAcceptedHttpResult("/commands/status"));
        });

        // Act
        using var job = await host.Client.PostAsync(new Uri("/jobs", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var command = await host.Client.PostAsync(new Uri("/commands", UriKind.Relative), null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, job.StatusCode);
        Assert.Equal("/jobs/3", job.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Accepted, command.StatusCode);
        Assert.Equal("/commands/status", command.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ToHttpResult_WhenFailure_TypeIsTheRfc9110SectionNotAThirdPartySite()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/orders/9", () => Result.Failure<Order>(OrderNotFound()).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/orders/9", UriKind.Relative), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.5", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToHttpResult_WhenValidationFails_TypeIsTheRfc4918Section()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => Result.Failure(ValidationError.Required("Email")).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("https://tools.ietf.org/html/rfc4918#section-11.2", json.RootElement.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(ErrorCategory.TooManyRequests)]
    [InlineData(ErrorCategory.Cancelled)]
    public async Task ToHttpResult_WhenStatusHasNoKnownSection_TypeIsAbsent(ErrorCategory category)
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/x", () => Result.Failure<int>(new CategorizedError(ErrorCode.From("TEST.X"), "Failed.", category)).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/x", UriKind.Relative), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert: RFC 9457 reads an absent type as about:blank
        Assert.False(json.RootElement.TryGetProperty("type", out _));
    }

    [Fact]
    public async Task ToHttpResult_WhenErrorIsTimeout_Answers504()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/quote", () => Result.Failure<int>(Error.Timeout(ErrorCode.Timeout, "The pricing service did not respond in time.")).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/quote", UriKind.Relative), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, ResultProblemDetailsFactory.ToStatusCode(ErrorCategory.Cancelled));
    }

    [Fact]
    public async Task NonGenericResult_WhenReturnedAsIs_IsSerializedWithoutMetadata()
    {
        // Arrange: no converter registered; Result carries its own
        var error = new SecretMetadataError();
        await using var host = await ResultHttpHost.StartAsync(app =>
        {
            app.MapGet("/fail", () => Result.Failure(error));
            app.MapGet("/ok", () => Result.Success());
        });

        // Act
        using var failure = await host.Client.GetAsync(new Uri("/fail", UriKind.Relative), TestContext.Current.CancellationToken);
        string failureBody = await failure.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var success = await host.Client.GetAsync(new Uri("/ok", UriKind.Relative), TestContext.Current.CancellationToken);
        string successBody = await success.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("metadata", failureBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal-host-42", failureBody, StringComparison.OrdinalIgnoreCase);
        using (var json = JsonDocument.Parse(failureBody))
        {
            Assert.False(json.RootElement.GetProperty("isSuccess").GetBoolean());
            Assert.Equal("TEST.SECRET", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal("""{"isSuccess":true}""", successBody);
    }

    private sealed record SecretMetadataError() : Error(ErrorCode.From("TEST.SECRET"), "Something failed.")
    {
        public override ErrorCategory Category => ErrorCategory.Technical;

        public override IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Create("host", "internal-host-42");
    }
}
