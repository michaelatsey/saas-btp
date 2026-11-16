
namespace MicroKit.Persistence.Tests.Results;

using MicroKit.Result;

/// <summary>#197 phase 3: the codes and messages of the Result path, and the default error name.</summary>
public sealed class PersistenceErrorsTests
{
    [Theory]
    [InlineData(typeof(Order), "ORDER")]
    [InlineData(typeof(OrderLine), "ORDER_LINE")]
    [InlineData(typeof(HTTPRequest), "HTTP_REQUEST")]
    [InlineData(typeof(Order2), "ORDER2")]
    [InlineData(typeof(V2Order), "V2_ORDER")]
    [InlineData(typeof(Generic<int>), "GENERIC")]
    public void DefaultErrorName_IsTheSimpleClrNameInUpperSnake(Type type, string expected)
    {
        Assert.Equal(expected, PersistenceErrors.DefaultErrorName(type));
    }

    [Theory]
    [InlineData("ORDER", true)]
    [InlineData("SHOP_ORDER", true)]
    [InlineData("ORDER2", true)]
    [InlineData("order", false)]
    [InlineData("_ORDER", false)]
    [InlineData("ORDER_", false)]
    [InlineData("SHOP__ORDER", false)]
    [InlineData("2ORDER", false)]
    [InlineData("SHOP.ORDER", false)]
    [InlineData("COMMANDÉ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidErrorName_AcceptsUpperSnakeOnly(string? name, bool valid)
    {
        Assert.Equal(valid, PersistenceErrors.IsValidErrorName(name));
    }

    [Fact]
    public void Errors_WithAName_AreCodedWithTheRoot()
    {
        Assert.Equal("PERSISTENCE.ORDER.DUPLICATE", PersistenceErrors.Duplicate("ORDER").Code.Value);
        Assert.Equal("PERSISTENCE.ORDER.REFERENCE", PersistenceErrors.Reference("ORDER").Code.Value);
        Assert.Equal("PERSISTENCE.ORDER.CONCURRENCY", PersistenceErrors.Concurrency("ORDER").Code.Value);
    }

    [Fact]
    public void Errors_WithoutAName_AreCodedWithTheReasonOnly()
    {
        Assert.Equal("PERSISTENCE.DUPLICATE", PersistenceErrors.Duplicate().Code.Value);
        Assert.Equal("PERSISTENCE.REFERENCE", PersistenceErrors.Reference().Code.Value);
        Assert.Equal("PERSISTENCE.CONCURRENCY", PersistenceErrors.Concurrency().Code.Value);
    }

    [Fact]
    public void Errors_AreConflicts_WithAMessageThatNamesNoRoot()
    {
        var error = PersistenceErrors.Concurrency("ORDER");

        Assert.Equal(ErrorCategory.Conflict, error.Category);
        Assert.DoesNotContain("ORDER", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PersistenceErrors.Concurrency("ORDER"), error);
    }

    [Fact]
    public void Errors_WithAnInvalidName_Throw()
    {
        Assert.Throws<ArgumentException>(() => PersistenceErrors.Duplicate("order"));
    }

    [Fact]
    public void AggregateVersionConflictException_IsAnInvalidOperationException_CarryingTheName()
    {
        var conflict = new AggregateVersionConflictException("stale") { ErrorName = "ORDER" };

        Assert.IsAssignableFrom<InvalidOperationException>(conflict);
        Assert.Equal("ORDER", conflict.ErrorName);
        Assert.Null(new AggregateVersionConflictException("stale").ErrorName);
        Assert.Null(new AggregateVersionConflictException("stale", null).InnerException);
        Assert.Throws<ArgumentException>(() => new AggregateVersionConflictException("stale") { ErrorName = "order" });
    }

    private sealed class Order;

    private sealed class OrderLine;

    private sealed class HTTPRequest;

    private sealed class Order2;

    private sealed class V2Order;

    private sealed class Generic<T>;
}
