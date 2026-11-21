using System.Text.Json;
using MicroKit.Result.Serialization;
using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Serialization;

// #198 review: the converters' switch selects a view, Client (the default) or Full, not only whether metadata is
// written.
public sealed class ResultJsonViewTests
{
    private static readonly IError Fault = Error.Technical(ErrorCode.From("DB.FAIL"), "Host db-7 refused the login of sa");

    private static readonly JsonSerializerOptions DefaultFactory = new() { Converters = { new ResultJsonConverterFactory() } };
    private static readonly JsonSerializerOptions ClientFactory = new() { Converters = { new ResultJsonConverterFactory(ResultJsonView.Client) } };
    private static readonly JsonSerializerOptions DefaultClosed = new() { Converters = { new ResultJsonConverter<int>() } };
    private static readonly JsonSerializerOptions ClientClosed = new() { Converters = { new ResultJsonConverter<int>(ResultJsonView.Client) } };
    private static readonly JsonSerializerOptions FullFactory = new() { Converters = { new ResultJsonConverterFactory(ResultJsonView.Full) } };
    private static readonly JsonSerializerOptions FullClosed = new() { Converters = { new ResultJsonConverter<int>(ResultJsonView.Full) } };
    private static readonly JsonSerializerOptions FullNonGeneric = new() { Converters = { new ResultJsonConverter(ResultJsonView.Full) } };

    public static TheoryData<string> ClientOptions() => new() { nameof(DefaultFactory), nameof(ClientFactory), nameof(DefaultClosed), nameof(ClientClosed) };

    public static TheoryData<string> FullOptions() => new() { nameof(FullFactory), nameof(FullClosed) };

    [Fact]
    public void Default_OfResultJsonView_IsClient()
    {
        // Act
        var view = default(ResultJsonView);

        // Assert
        Assert.Equal(ResultJsonView.Client, view);
    }

    [Theory]
    [MemberData(nameof(ClientOptions))]
    public void Write_WithTheClientView_RedactsTheFaultAndOmitsMetadata(string name)
    {
        // Arrange
        var options = Options(name);

        // Act
        var faultJson = JsonSerializer.Serialize(Result.Failure<int>(Fault), options);
        var metadataJson = JsonSerializer.Serialize(Result.Failure<int>(new CriticalTestError()), options);

        // Assert
        Assert.DoesNotContain("db-7", faultJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("metadata", metadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(FullOptions))]
    public void Write_WithTheFullView_KeepsTheFaultAndWritesMetadata(string name)
    {
        // Arrange
        var options = Options(name);

        // Act
        var faultJson = JsonSerializer.Serialize(Result.Failure<int>(Fault), options);
        var metadataJson = JsonSerializer.Serialize(Result.Failure<int>(new CriticalTestError()), options);

        // Assert
        Assert.Contains("Host db-7 refused the login of sa", faultJson);
        Assert.Contains("\"metadata\"", metadataJson);
    }

    [Fact]
    public void Write_WithTheFullViewOfTheNonGenericConverter_KeepsTheFault()
    {
        // Act
        var json = JsonSerializer.Serialize(Result.Failure(Fault), FullNonGeneric);

        // Assert
        Assert.Contains("Host db-7 refused the login of sa", json);
    }

    [Fact]
    public void Constructors_WhenViewIsUndefined_ThrowArgumentOutOfRangeException()
    {
        // Arrange
        const ResultJsonView undefined = (ResultJsonView)42;

        // Act + Assert
        Assert.Equal("view", Assert.Throws<ArgumentOutOfRangeException>(() => new ResultJsonConverterFactory(undefined)).ParamName);
        Assert.Equal("view", Assert.Throws<ArgumentOutOfRangeException>(() => new ResultJsonConverter<int>(undefined)).ParamName);
        Assert.Equal("view", Assert.Throws<ArgumentOutOfRangeException>(() => new ResultJsonConverter(undefined)).ParamName);
    }

    private static JsonSerializerOptions Options(string name) => name switch
    {
        nameof(DefaultFactory) => DefaultFactory,
        nameof(ClientFactory) => ClientFactory,
        nameof(DefaultClosed) => DefaultClosed,
        nameof(ClientClosed) => ClientClosed,
        nameof(FullFactory) => FullFactory,
        nameof(FullClosed) => FullClosed,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
