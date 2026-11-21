using System.Reflection;

namespace MicroKit.Result.Tests.Errors;

public sealed class ErrorCodeTests
{
    [Fact]
    public void From_WhenValueIsValid_ReturnsCodeWithValue()
    {
        // Arrange
        const string value = "AUTH.USER.NOT_FOUND";

        // Act
        var code = ErrorCode.From(value);

        // Assert
        Assert.Equal(value, code.Value);
    }

    [Fact]
    public void From_WhenValueIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        string? value = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => ErrorCode.From(value!));

        // Assert
        Assert.Equal("value", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void From_WhenValueIsEmptyOrWhitespace_ThrowsArgumentException(string value)
    {
        // Arrange + Act
        var exception = Assert.Throws<ArgumentException>(() => ErrorCode.From(value));

        // Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Equal("value", exception.ParamName);
    }

    [Theory]
    [InlineData(nameof(ErrorCode.Failure), "FAILURE")]
    [InlineData(nameof(ErrorCode.Validation), "VALIDATION")]
    [InlineData(nameof(ErrorCode.NotFound), "NOT_FOUND")]
    [InlineData(nameof(ErrorCode.Unauthorized), "UNAUTHORIZED")]
    [InlineData(nameof(ErrorCode.Forbidden), "FORBIDDEN")]
    [InlineData(nameof(ErrorCode.Conflict), "CONFLICT")]
    [InlineData(nameof(ErrorCode.NotSupported), "NOT_SUPPORTED")]
    [InlineData(nameof(ErrorCode.BadRequest), "BAD_REQUEST")]
    [InlineData(nameof(ErrorCode.Timeout), "TIMEOUT")]
    [InlineData(nameof(ErrorCode.Internal), "INTERNAL")]
    [InlineData(nameof(ErrorCode.PreconditionFailed), "PRECONDITION_FAILED")]
    [InlineData(nameof(ErrorCode.ServiceUnavailable), "SERVICE_UNAVAILABLE")]
    [InlineData(nameof(ErrorCode.Cancelled), "CANCELLED")]
    [InlineData(nameof(ErrorCode.TooManyRequests), "TOO_MANY_REQUESTS")]
    [InlineData(nameof(ErrorCode.Exception), "EXCEPTION")]
    [InlineData(nameof(ErrorCode.NullOrEmpty), "NULL_OR_EMPTY")]
    [InlineData(nameof(ErrorCode.InvalidFormat), "INVALID_FORMAT")]
    [InlineData(nameof(ErrorCode.Duplicate), "DUPLICATE")]
    [InlineData(nameof(ErrorCode.ExternalServiceFailure), "EXTERNAL_SERVICE_FAILURE")]
    public void PredefinedCode_WhenRead_HasExpectedValue(string fieldName, string expected)
    {
        // Arrange
        var field = typeof(ErrorCode).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);

        // Act
        var code = (ErrorCode)field.GetValue(null)!;

        // Assert
        Assert.Equal(expected, code.Value);
    }

    [Fact]
    public void PredefinedCodes_WhenEnumerated_AreAllCoveredByTheory()
    {
        // Arrange
        var fields = typeof(ErrorCode)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ErrorCode));

        // Act
        var count = fields.Count();

        // Assert
        Assert.Equal(19, count);
    }

    [Fact]
    public void ImplicitString_WhenConverted_ReturnsValue()
    {
        // Arrange
        var code = ErrorCode.From("ORDER.PAYMENT.DECLINED");

        // Act
        string raw = code;

        // Assert
        Assert.Equal("ORDER.PAYMENT.DECLINED", raw);
    }

    [Fact]
    public void ToString_WhenCalled_ReturnsValue()
    {
        // Arrange
        var code = ErrorCode.From("USER.NOT_FOUND");

        // Act
        var text = code.ToString();

        // Assert
        Assert.Equal("USER.NOT_FOUND", text);
    }

    [Theory]
    [InlineData("A", "B", -1)]
    [InlineData("B", "A", 1)]
    [InlineData("A", "A", 0)]
    [InlineData("Z", "a", -1)]
    [InlineData("a", "Z", 1)]
    public void CompareTo_WhenCompared_UsesOrdinalOrder(string left, string right, int expectedSign)
    {
        // Arrange
        var first = ErrorCode.From(left);
        var second = ErrorCode.From(right);

        // Act
        var comparison = first.CompareTo(second);

        // Assert
        Assert.Equal(expectedSign, Math.Sign(comparison));
    }

    [Theory]
    [InlineData("A", "B", true, true, false, false)]
    [InlineData("B", "A", false, false, true, true)]
    [InlineData("A", "A", false, true, false, true)]
    public void ComparisonOperators_WhenCompared_FollowCompareTo(
        string left,
        string right,
        bool lessThan,
        bool lessThanOrEqual,
        bool greaterThan,
        bool greaterThanOrEqual)
    {
        // Arrange
        var first = ErrorCode.From(left);
        var second = ErrorCode.From(right);

        // Act
        var lt = first < second;
        var le = first <= second;
        var gt = first > second;
        var ge = first >= second;

        // Assert
        Assert.Equal(lessThan, lt);
        Assert.Equal(lessThanOrEqual, le);
        Assert.Equal(greaterThan, gt);
        Assert.Equal(greaterThanOrEqual, ge);
    }

    [Fact]
    public void Equals_WhenSameValue_ReturnsTrue()
    {
        // Arrange
        var first = ErrorCode.From("USER.NOT_FOUND");
        var second = ErrorCode.From("USER.NOT_FOUND");

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.True(equal);
        Assert.True((first == second));
        Assert.Equal(second.GetHashCode(), first.GetHashCode());
    }

    [Fact]
    public void Equals_WhenValueDiffersOnlyByCase_ReturnsFalse()
    {
        // Arrange
        var first = ErrorCode.From("USER.NOT_FOUND");
        var second = ErrorCode.From("user.not_found");

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.False(equal);
        Assert.True((first != second));
    }

    [Fact]
    public void Sort_WhenCodesUnordered_OrdersOrdinally()
    {
        // Arrange
        ErrorCode[] codes = [ErrorCode.From("b"), ErrorCode.From("B"), ErrorCode.From("A")];

        // Act
        var sorted = codes.Order().Select(c => c.Value).ToArray();

        // Assert
        Assert.Equal(["A", "B", "b"], sorted);
    }
}
