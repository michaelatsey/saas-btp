using MicroKit.Domain.Identifiers;
using Xunit;

namespace MicroKit.Domain.Tests.Identifiers;

public sealed class EntityIdTests
{
    [Fact]
    public void Constructor_EmptyGuid_ThrowsArgumentException()
    {
        Assert.Equal("value", Assert.Throws<ArgumentException>(() => new CustomerId(Guid.Empty)).ParamName);
    }

    [Fact]
    public void Constructor_ValidGuid_KeepsTheValue()
    {
        var value = Guid.CreateVersion7();

        var id = new CustomerId(value);

        Assert.Equal(value, id.Value);
    }

    [Fact]
    public void ToString_OfADerivedIdentifier_IsTheGuidValue()
    {
        // A derived record would otherwise synthesize "CustomerId { Value = … }".
        var value = Guid.CreateVersion7();

        Assert.Equal(value.ToString(), new CustomerId(value).ToString());
    }

    [Fact]
    public void Equality_SameOrOtherIdentifierType_IsByValueAndType()
    {
        var value = Guid.CreateVersion7();

        Assert.Equal(new CustomerId(value), new CustomerId(value));
        Assert.NotEqual(new CustomerId(Guid.CreateVersion7()), new CustomerId(value));
        Assert.False(new CustomerId(value).Equals(new SupplierId(value)));
    }
}

public sealed record CustomerId : EntityId<CustomerId>
{
    public CustomerId(Guid value) : base(value) { }
}

public sealed record SupplierId : EntityId<SupplierId>
{
    public SupplierId(Guid value) : base(value) { }
}
