namespace MicroKit.Result.Tests.Core;

public sealed class UnitTests
{
    [Fact]
    public void Value_WhenRead_EqualsDefault()
    {
        // Act
        var value = Unit.Value;

        // Assert
        Assert.Equal(default(Unit), value);
        Assert.Equal(new Unit(), value);
    }

    [Fact]
    public void Equals_WhenTwoUnits_ReturnsTrue()
    {
        // Arrange
        var left = Unit.Value;
        var right = new Unit();

        // Act
        var equal = left.Equals(right);

        // Assert
        Assert.True(equal);
        Assert.True((left == right));
        Assert.False((left != right));
        Assert.Equal(right.GetHashCode(), left.GetHashCode());
    }

    [Fact]
    public void EqualsObject_WhenBoxedUnit_ReturnsTrue()
    {
        // Arrange
        object boxed = new Unit();

        // Act
        var equal = Unit.Value.Equals(boxed);

        // Assert
        Assert.True(equal);
    }

    [Fact]
    public void EqualsObject_WhenNullOrOtherType_ReturnsFalse()
    {
        // Act
        var equalsNull = Unit.Value.Equals(null);
        var equalsOther = Unit.Value.Equals((object)0);

        // Assert
        Assert.False(equalsNull);
        Assert.False(equalsOther);
    }

    [Fact]
    public void ToString_WhenCalled_ReturnsEmptyParentheses()
    {
        // Act
        var text = Unit.Value.ToString();

        // Assert
        Assert.Equal("()", text);
    }

    [Fact]
    public void Success_WhenValueIsUnit_IsSuccessWithUnit()
    {
        // Act
        Result<Unit> result = Result.Success(Unit.Value);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(Unit.Value, result.Value);
    }
}
