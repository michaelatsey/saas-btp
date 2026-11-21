using System.Net;
using System.Text.Json;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;

namespace MicroKit.Result.AspNetCore.IntegrationTests.Http;

// #195 item 1: a 5xx response never carries the error's message. Its detail is a generic, client-safe text, the
// same for every server category; errorCode (and errorCodes for a collection) is kept. A 4xx response keeps the
// error's message as its detail.
public sealed class ServerErrorDetailTests
{
    private const string KnownText = "Npgsql: connection to 10.0.0.12:5432 refused";

    private const string OtherKnownText = "Redis: READONLY You can't write against a read only replica at 10.0.0.31:6379";

    [Fact]
    public async Task ToHttpResult_WhenTechnicalErrorCarriesAnInternalText_ResponseOmitsItAndKeepsTheCode()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/orders", () => Result.Failure<int>(Error.Technical(ErrorCode.From("DB.FAIL"), KnownText)).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/orders", UriKind.Relative), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(KnownText, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("10.0.0.12", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("DB.FAIL", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData(ErrorCategory.Technical, HttpStatusCode.InternalServerError)]
    [InlineData(ErrorCategory.NotSupported, HttpStatusCode.NotImplemented)]
    [InlineData(ErrorCategory.External, HttpStatusCode.BadGateway)]
    [InlineData(ErrorCategory.Unavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ErrorCategory.Timeout, HttpStatusCode.GatewayTimeout)]
    [InlineData((ErrorCategory)999, HttpStatusCode.InternalServerError)]
    public async Task ToHttpResult_WhenCategoryMapsTo5xx_ResponseOmitsTheMessage(ErrorCategory category, HttpStatusCode expected)
    {
        // Arrange
        var error = new ServerTestError(category, KnownText);
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/quote", () => Result.Failure<int>(error).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/quote", UriKind.Relative), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain(KnownText, body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.False(string.IsNullOrWhiteSpace((json.RootElement.GetProperty("detail").GetString() ?? string.Empty)));
        Assert.Equal("TEST.SERVER", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData(ErrorCategory.Technical)]
    [InlineData(ErrorCategory.NotSupported)]
    [InlineData(ErrorCategory.External)]
    [InlineData(ErrorCategory.Unavailable)]
    [InlineData(ErrorCategory.Timeout)]
    [InlineData((ErrorCategory)999)]
    public void CreateProblemDetails_WhenCategoryMapsTo5xx_DetailIsGenericAndCodeIsKept(ErrorCategory category)
    {
        // Arrange
        var error = new ServerTestError(category, KnownText);

        // Act
        var problem = ResultProblemDetailsFactory.CreateProblemDetails(error);

        // Assert
        string detail = problem.Detail ?? string.Empty;
        Assert.True((problem.Status >= 500));
        Assert.False(string.IsNullOrWhiteSpace(detail));
        Assert.DoesNotContain(KnownText, detail, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(error.Message, detail);
        Assert.Equal("TEST.SERVER", problem.Extensions["errorCode"]);
    }

    [Fact]
    public void CreateProblemDetails_ForEvery5xxCategory_DetailIsTheSameText()
    {
        // Arrange: every category the factory maps to 5xx, an undefined value included
        var serverCategories = Enum.GetValues<ErrorCategory>()
            .Append((ErrorCategory)999)
            .Where(c => ResultProblemDetailsFactory.ToStatusCode(c) >= 500)
            .ToArray();

        // Act
        var details = serverCategories
            .Select(c => ResultProblemDetailsFactory.CreateProblemDetails(new ServerTestError(c, $"{KnownText} ({c})")).Detail)
            .ToArray();

        // Assert
        // The same categories, whatever their order.
        Assert.Equal(
            new[] { ErrorCategory.Technical, ErrorCategory.Unavailable, ErrorCategory.External, ErrorCategory.NotSupported, ErrorCategory.Timeout, (ErrorCategory)999 }.Order(),
            serverCategories.Order());
        Assert.All(details, d => Assert.True(!string.IsNullOrWhiteSpace(d)));
        Assert.All(details, d => Assert.True(!d!.Contains(KnownText, StringComparison.Ordinal))); // Safe: every detail checked non-null on the line above
        Assert.Single(details.Distinct(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(ErrorCategory.NotFound)]
    [InlineData(ErrorCategory.Validation)]
    [InlineData(ErrorCategory.Unauthorized)]
    [InlineData(ErrorCategory.Forbidden)]
    [InlineData(ErrorCategory.Conflict)]
    [InlineData(ErrorCategory.TooManyRequests)]
    [InlineData(ErrorCategory.BusinessRule)]
    [InlineData(ErrorCategory.Cancelled)]
    [InlineData(ErrorCategory.PreconditionFailed)]
    public void CreateProblemDetails_WhenCategoryMapsTo4xx_DetailIsTheErrorMessage(ErrorCategory category)
    {
        // Arrange
        var error = new ServerTestError(category, "The order 7 cannot be shipped yet.");

        // Act
        var problem = ResultProblemDetailsFactory.CreateProblemDetails(error);

        // Assert
        Assert.True((problem.Status < 500));
        Assert.Equal(error.Message, problem.Detail);
        Assert.Equal("TEST.SERVER", problem.Extensions["errorCode"]);
    }

    [Fact]
    public async Task ToHttpResult_WhenCollectionIs5xx_ResponseOmitsEveryMessageAndKeepsTheCodes()
    {
        // Arrange
        var errors = ErrorCollection.From(
            Error.Technical(ErrorCode.From("DB.FAIL"), KnownText),
            Error.Technical(ErrorCode.From("CACHE.FAIL"), OtherKnownText));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(KnownText, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OtherKnownText, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Redis", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.False(string.IsNullOrWhiteSpace((json.RootElement.GetProperty("detail").GetString() ?? string.Empty)));
        Assert.Equal(["DB.FAIL", "CACHE.FAIL"], json.RootElement.GetProperty("errorCodes").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public void CreateProblemDetails_WhenCollectionIs5xx_DetailIsTheGenericServerText()
    {
        // Arrange
        var errors = ErrorCollection.From(
            Error.Technical(ErrorCode.From("DB.FAIL"), KnownText),
            ValidationError.Required("Email"),
            Error.Technical(ErrorCode.From("CACHE.FAIL"), OtherKnownText));
        string? generic = ResultProblemDetailsFactory.CreateProblemDetails(new ServerTestError(ErrorCategory.Technical, "Single.")).Detail;

        // Act
        var problem = ResultProblemDetailsFactory.CreateProblemDetails(errors);

        // Assert
        string detail = problem.Detail ?? string.Empty;
        Assert.Equal(500, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(detail));
        Assert.Equal(generic, detail);
        Assert.DoesNotContain(KnownText, detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OtherKnownText, detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["DB.FAIL", "CACHE.FAIL"], Assert.IsType<string[]>(problem.Extensions["errorCodes"]));
    }

    // Its category is a constructor parameter, an undefined value included (CategorizedError rejects one).
    private sealed record ServerTestError(ErrorCategory Kind, string Text)
        : Error(ErrorCode.From("TEST.SERVER"), Text)
    {
        public override ErrorCategory Category => Kind;
    }

    // #198 review: one definition of a server fault, and of its text, owned by the core

    [Theory]
    [InlineData(ErrorCategory.NotFound)]
    [InlineData(ErrorCategory.Validation)]
    [InlineData(ErrorCategory.Unauthorized)]
    [InlineData(ErrorCategory.Forbidden)]
    [InlineData(ErrorCategory.Conflict)]
    [InlineData(ErrorCategory.TooManyRequests)]
    [InlineData(ErrorCategory.Technical)]
    [InlineData(ErrorCategory.Unavailable)]
    [InlineData(ErrorCategory.External)]
    [InlineData(ErrorCategory.BusinessRule)]
    [InlineData(ErrorCategory.NotSupported)]
    [InlineData(ErrorCategory.Timeout)]
    [InlineData(ErrorCategory.Cancelled)]
    [InlineData(ErrorCategory.PreconditionFailed)]
    [InlineData((ErrorCategory)999)]
    public void ToStatusCode_ForEveryCategory_Is5xxExactlyWhenTheCoreCallsItASystemFault(ErrorCategory category)
    {
        // Act
        var is5xx = ResultProblemDetailsFactory.ToStatusCode(category) >= 500;

        // Assert
        Assert.Equal(category.IsSystemFault(), is5xx);
    }

    [Fact]
    public void CreateProblemDetails_WhenSystemFault_DetailIsTheCoreSystemFaultMessage()
    {
        // Act
        var problem = ResultProblemDetailsFactory.CreateProblemDetails(Error.Technical(ErrorCode.From("DB.FAIL"), "internal"));

        // Assert
        Assert.Equal(ErrorCategoryExtensions.SystemFaultMessage, problem.Detail);
    }
}
