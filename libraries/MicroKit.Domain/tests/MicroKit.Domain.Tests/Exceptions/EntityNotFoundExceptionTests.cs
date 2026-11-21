using MicroKit.Domain.Exceptions;
using MicroKit.Domain.Identifiers;
using Xunit;

namespace MicroKit.Domain.Tests.Exceptions;

public sealed class EntityNotFoundExceptionTests
{
    [Fact]
    public void Constructor_ValidIdentifier_CarriesTheIdentifierAndTheEntityType()
    {
        var id = GuidId.From(Guid.CreateVersion7());

        var ex = new EntityNotFoundException<MissingOrder, GuidId>(id);

        Assert.Equal(id, ex.EntityId);
        Assert.Equal(typeof(MissingOrder), ex.EntityType);
        Assert.Equal($"MissingOrder with ID '{id}' was not found.", ex.Message);
        Assert.IsAssignableFrom<DomainException>(ex);
    }

    [Fact]
    public void Message_WithADerivedEntityId_NamesTheGuidValue()
    {
        var value = Guid.CreateVersion7();

        Assert.Equal($"MissingOrder with ID '{value}' was not found.", new EntityNotFoundException<MissingOrder, MissingOrderId>(new MissingOrderId(value)).Message);
    }
}

public sealed class MissingOrder;

public sealed record MissingOrderId : EntityId<MissingOrderId>
{
    public MissingOrderId(Guid value) : base(value) { }
}
