using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using Xunit;

namespace MicroKit.Domain.Tests.Aggregates;

public sealed class EntityTests
{
    [Fact]
    public void Constructor_DefaultStructId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => new StructIdEntity(default));

        Assert.Equal("id", ex.ParamName);
    }

    [Fact]
    public void Constructor_EmptyGuidId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new StructIdEntity(GuidId.From(Guid.Empty)));
    }

    [Fact]
    public void Constructor_NullClassId_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new ClassIdEntity(null!));

        Assert.Equal("id", ex.ParamName);
    }

    [Fact]
    public void Constructor_NonDefaultStructId_SetsId()
    {
        var id = GuidId.New();

        Assert.Equal(id, new StructIdEntity(id).Id);
    }

    [Fact]
    public void Equals_SubclassWithSameId_IsEqualBothWays()
    {
        var id = GuidId.New();
        Entity<GuidId> entity = new StructIdEntity(id);
        Entity<GuidId> proxy = new StructIdEntityProxy(id);

        Assert.True(entity.Equals(proxy));
        Assert.True(proxy.Equals(entity));
        Assert.True(entity.Equals((object)proxy));
        Assert.True(proxy.Equals((object)entity));
        Assert.True((entity == proxy));
        Assert.True((proxy == entity));
        Assert.Equal(proxy.GetHashCode(), entity.GetHashCode());
    }

    [Fact]
    public void Equals_UnrelatedEntityTypesWithSameId_AreNotEqual()
    {
        var id = GuidId.New();
        Entity<GuidId> left = new StructIdEntity(id);
        Entity<GuidId> right = new OtherStructIdEntity(id);

        Assert.False(left.Equals(right));
        Assert.False(right.Equals(left));
        Assert.False((left == right));
        Assert.True((left != right));
    }

    [Fact]
    public void Equals_SameTypeWithDifferentIds_AreNotEqual()
    {
        var left = new StructIdEntity(GuidId.New());
        var right = new StructIdEntity(GuidId.New());

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var entity = new StructIdEntity(GuidId.New());

        Assert.False(entity.Equals(null));
        Assert.False((entity == null));
    }

    [Fact]
    public void Equals_ClassIdsWithSameValue_AreEqual()
    {
        var value = Guid.NewGuid();

        Assert.Equal(new ClassIdEntity(new ClassId(value)), new ClassIdEntity(new ClassId(value)));
    }
}

internal sealed record ClassId(Guid Value) : IEntityId;

internal class StructIdEntity(GuidId id) : Entity<GuidId>(id);

// Stands in for an EF Core proxy: a runtime subclass of the mapped entity.
internal sealed class StructIdEntityProxy(GuidId id) : StructIdEntity(id);

internal sealed class OtherStructIdEntity(GuidId id) : Entity<GuidId>(id);

internal sealed class ClassIdEntity(ClassId id) : Entity<ClassId>(id);
