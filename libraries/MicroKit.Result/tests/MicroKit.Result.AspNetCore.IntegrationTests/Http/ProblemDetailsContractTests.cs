using System.Net;
using System.Text.Json;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;

namespace MicroKit.Result.AspNetCore.IntegrationTests.Http;

// #184 Phase 2: what a client receives when an endpoint returns a failure through ToHttpResult().
public sealed class ProblemDetailsContractTests
{
    private const string SecretMessage = "Npgsql: password authentication failed for user 'svc' at /srv/app/secrets.json";

    [Fact]
    public async Task ToHttpResult_WhenErrorWrapsAnException_ResponseOmitsTheExceptionMessage()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/boom", () => Result<int>.Try(() => throw new InvalidOperationException(SecretMessage)).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/boom", UriKind.Relative), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(SecretMessage, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        // #195 item 1: a 5xx response carries the generic server text, the same for every server error
        string? genericServerDetail = ResultProblemDetailsFactory
            .CreateProblemDetails(Error.Technical(ErrorCode.From("TEST.TECHNICAL"), "Any technical message.")).Detail;
        Assert.Equal(genericServerDetail, json.RootElement.GetProperty("detail").GetString());
        Assert.Equal("EXCEPTION", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ToHttpResult_WhenErrorIsOneValidationError_ResponseGroupsItUnderItsProperty()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => Result.Failure(ValidationError.InvalidEmail()).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var errors = json.RootElement.GetProperty("errors");
        Assert.Equal(["Email"], errors.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["Email is not a valid email address."], errors.GetProperty("Email").EnumerateArray().Select(m => m.GetString()));
    }

    [Fact]
    public async Task ToHttpResult_WhenErrorIsACollection_ResponseGroupsValidationMessagesByProperty()
    {
        // Arrange
        var validation = new ValidationResult()
            .AddError("Email", "Email is required.")
            .AddError("Password", "Password must be at least 8 characters.")
            .AddError("Email", "Email is not a valid email address.");
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => validation.ToResult().ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var errors = json.RootElement.GetProperty("errors");
        Assert.Equal(["Email is required.", "Email is not a valid email address."], errors.GetProperty("Email").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(["Password must be at least 8 characters."], errors.GetProperty("Password").EnumerateArray().Select(m => m.GetString()));
    }

    [Fact]
    public async Task ToHttpResult_WhenCollectionMixesValidationAndTechnical_ResponseIs500()
    {
        // Arrange
        var errors = ErrorCollection.From(ValidationError.Required("Name"), new CategorizedTestError(ErrorCategory.Technical));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task ToHttpResult_WhenErrorIsNotValidation_ResponseHasNoErrorsMember()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/items/1", () => Result.Failure<int>(new CategorizedTestError(ErrorCategory.NotFound)).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/items/1", UriKind.Relative), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(json.RootElement.TryGetProperty("errors", out _));
    }

    public static TheoryData<ErrorCategory> Categories()
    {
        var data = new TheoryData<ErrorCategory>();
        foreach (var category in Enum.GetValues<ErrorCategory>())
            data.Add(category);
        data.Add((ErrorCategory)999);
        return data;
    }

    public static TheoryData<ErrorCategory, ErrorCategory> CategoryPairs()
    {
        var categories = Enum.GetValues<ErrorCategory>().Append((ErrorCategory)999).ToArray();
        var data = new TheoryData<ErrorCategory, ErrorCategory>();
        foreach (var first in categories)
            foreach (var second in categories)
                data.Add(first, second);
        return data;
    }

    // The category, hence the status, of an ErrorCollection never depends on the order of its errors.
    [Theory]
    [MemberData(nameof(CategoryPairs))]
    public void ErrorCollectionCategory_ForEveryPairInBothOrders_GivesTheSameStatus(ErrorCategory first, ErrorCategory second)
    {
        // Arrange
        var forward = ErrorCollection.From(new CategorizedTestError(first), new CategorizedTestError(second));
        var backward = ErrorCollection.From(new CategorizedTestError(second), new CategorizedTestError(first));

        // Act
        int forwardStatus = ResultProblemDetailsFactory.ToStatusCode(forward.Category);
        int backwardStatus = ResultProblemDetailsFactory.ToStatusCode(backward.Category);

        // Assert
        Assert.Equal(backward.Category, forward.Category);
        Assert.Equal(backwardStatus, forwardStatus);
    }

    // A server fault is never hidden behind a client error.
    [Theory]
    [MemberData(nameof(Categories))]
    public void ErrorCollectionCategory_WhenOneErrorMapsTo5xx_Answers5xx(ErrorCategory category)
    {
        // Arrange
        var collection = ErrorCollection.From(ValidationError.Required("Name"), new CategorizedTestError(category), new CategorizedTestError(ErrorCategory.NotFound));

        // Act
        bool mapsTo5xx = ResultProblemDetailsFactory.ToStatusCode(category) >= 500;

        // Assert
        Assert.Equal(mapsTo5xx, (ResultProblemDetailsFactory.ToStatusCode(collection.Category) >= 500));
    }

    [Theory]
    [InlineData(ErrorCategory.Unauthorized, HttpStatusCode.Unauthorized)]
    [InlineData(ErrorCategory.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ErrorCategory.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(ErrorCategory.Conflict, HttpStatusCode.Conflict)]
    public async Task ToHttpResult_WhenCollectionMixesValidationWithAClientError_AnswersThatErrorWithoutFieldErrors(
        ErrorCategory category, HttpStatusCode expected)
    {
        // Arrange: the validation error comes first, so order cannot decide the status
        var errors = ErrorCollection.From(ValidationError.Required("Email"), new CategorizedTestError(category));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert: an unauthenticated caller, or a missing resource, never receives field validation detail
        Assert.Equal(expected, response.StatusCode);
        Assert.False(json.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ToHttpResult_WhenCollectionMixesValidationAndBusinessRule_AnswersFieldErrors()
    {
        // Arrange
        var errors = ErrorCollection.From(new CategorizedTestError(ErrorCategory.BusinessRule), ValidationError.Required("Email"));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("Email", out _));
    }

    [Fact]
    public async Task ToHttpResult_WhenCollectionMixesValidationAndTechnical_ResponseCarriesNoFieldErrors()
    {
        // Arrange
        var errors = ErrorCollection.From(ValidationError.Required("Name"), new CategorizedTestError(ErrorCategory.Technical));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert: a 500 gives the client nothing to correct
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(json.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ToHttpResult_WhenValidationCollectionIsNested_ResponseListsEveryCodeInOrder()
    {
        // Arrange
        var inner = ErrorCollection.From(new CategorizedTestError(ErrorCategory.BusinessRule), ValidationError.Required("Name"));
        var errors = ErrorCollection.From(ValidationError.Required("Email"), inner);
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert: a Validation or BusinessRule response names every error
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["VALIDATION.EMAIL", "TEST.CATEGORIZED", "VALIDATION.NAME"], json.RootElement.GetProperty("errorCodes").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal("Email is required.; A categorized test error.; Name is required.", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ToHttpResult_WhenNonValidationCollectionIsNested_ResponseListsOnlyTheChosenCategorysCodesInOrder()
    {
        // Arrange
        var inner = ErrorCollection.From(new CategorizedTestError(ErrorCategory.Conflict), ValidationError.Required("Name"));
        var errors = ErrorCollection.From(new CategorizedTestError(ErrorCategory.Conflict), inner, new CategorizedTestError(ErrorCategory.Cancelled));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert: nested collections are flattened, and only the Conflict errors are named, not the Validation or Cancelled one
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(["TEST.CATEGORIZED", "TEST.CATEGORIZED"], json.RootElement.GetProperty("errorCodes").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal("A categorized test error.; A categorized test error.", json.RootElement.GetProperty("detail").GetString());
    }

    // A non-validation response says nothing of the request's input: no field errors, and neither detail nor
    // errorCodes name a validation error, whatever the order.
    [Theory]
    [InlineData(ErrorCategory.Unauthorized, HttpStatusCode.Unauthorized, false)]
    [InlineData(ErrorCategory.Unauthorized, HttpStatusCode.Unauthorized, true)]
    [InlineData(ErrorCategory.NotFound, HttpStatusCode.NotFound, false)]
    [InlineData(ErrorCategory.NotFound, HttpStatusCode.NotFound, true)]
    public async Task ToHttpResult_WhenCollectionMixesValidationWithAClientError_BodyNamesOnlyThatError(
        ErrorCategory category, HttpStatusCode expected, bool validationFirst)
    {
        // Arrange
        var clientError = new NamedTestError(category);
        IError[] mixed = validationFirst
            ? [ValidationError.Required("Email"), clientError, ValidationError.Required("Password")]
            : [clientError, ValidationError.Required("Email"), ValidationError.Required("Password")];
        var errors = ErrorCollection.From(mixed);
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);

        // Assert
        Assert.Equal(expected, response.StatusCode);
        Assert.False(json.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(clientError.Message, json.RootElement.GetProperty("detail").GetString());
        Assert.Equal([clientError.Code.Value], json.RootElement.GetProperty("errorCodes").EnumerateArray().Select(c => c.GetString()));
        Assert.DoesNotContain("VALIDATION.", body);
        Assert.DoesNotContain("Email", body);
        Assert.DoesNotContain("Password", body);
    }

    // The guide's MapError pattern (docs/guides/usage.md, "Work on the failure rail with MapError and Compensate"):
    // a collection is enriched error by error, so category filtering still applies.
    [Fact]
    public async Task ToHttpResult_WhenEnrichedCollectionMixesUnauthorizedAndValidation_BodyNamesNoValidationError()
    {
        // Arrange
        var unauthorized = new NamedTestError(ErrorCategory.Unauthorized);
        Result<int> failed = Result.Failure<int>(ErrorCollection.From(ValidationError.Required("Email"), unauthorized));
        Result<int> enriched = failed.MapError(e => EnrichedError.Enrich(e, "corr-1"));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => enriched.ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(json.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(unauthorized.Message, json.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain("VALIDATION.", body);
        Assert.DoesNotContain("Email", body);
        Assert.DoesNotContain("is required", body);
    }

    [Fact]
    public void CreateProblemDetails_WhenValidationErrorHasNullPropertyName_GroupsItUnderEmptyKey()
    {
        // Act
        var problem = ResultProblemDetailsFactory.CreateProblemDetails(new NullPropertyValidationError());

        // Assert
        Assert.Equal(["Invalid."], Assert.IsType<Microsoft.AspNetCore.Http.HttpValidationProblemDetails>(problem)
            .Errors[string.Empty]);
    }

    private sealed class NullPropertyValidationError : IValidationError
    {
        public string PropertyName => null!;

        public ErrorCode Code => ErrorCode.Validation;

        public string Message => "Invalid.";

        public ErrorCategory Category => ErrorCategory.Validation;

        public ErrorSeverity Severity => ErrorSeverity.Warning;

        public IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Empty;
    }

    // Copy of the guide's application wrapper (samples/MicroKit.Result.Samples/Guide/Composing.cs).
    private sealed record EnrichedError(IError Inner, string CorrelationId) : Error(Inner.Code, Inner.Message)
    {
        public override ErrorCategory Category => Inner.Category;

        public override ErrorSeverity Severity => Inner.Severity;

        public override IReadOnlyDictionary<string, object?> Metadata =>
            new ErrorMetadataBuilder().Merge(Inner.Metadata).WithCorrelationId(CorrelationId).Build();

        public static IError Enrich(IError error, string correlationId) => error is ErrorCollection collection
            ? ErrorCollection.From(collection.Select(e => Enrich(e, correlationId)))
            : new EnrichedError(error, correlationId);
    }

    private sealed record NamedTestError(ErrorCategory Kind)
    : Error(ErrorCode.From($"TEST.{Kind}".ToUpperInvariant()), $"The {Kind} test error.")
    {
        public override ErrorCategory Category => Kind;
    }

    private sealed record CategorizedTestError(ErrorCategory Kind)
    : Error(ErrorCode.From("TEST.CATEGORIZED"), "A categorized test error.")
    {
        public override ErrorCategory Category => Kind;
    }
}
