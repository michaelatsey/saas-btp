using System.Reflection;
using System.Runtime.CompilerServices;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using SaasBtp.Access.Domain.Founding;
using SaasBtp.Access.Domain.Memberships;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Domain.Tests;

/// <summary>
/// The identifiers of the founding (naming.md "Aggregate identifiers"): strongly typed value
/// objects, the organization's being the request's reference, every other a UUIDv7 set by the
/// founding.
/// </summary>
public sealed class IdentifierTests
{
    /// <summary>
    /// What the strong-typing check reads: the public instance properties each public type of the
    /// Access domain declares itself. Members inherited from MicroKit.Domain (the event's
    /// <c>EventId</c>, the aggregate's <c>DomainEvents</c>) are not the Access domain's own.
    /// </summary>
    private static readonly IReadOnlyList<(Type Type, PropertyInfo Property)> DeclaredProperties =
        [.. typeof(CompanyFounding).Assembly.GetExportedTypes()
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => (type, property)))];

    /// <summary>The identity provider's identifier of a person, not an identifier of this domain (ADR-ARCH-031).</summary>
    private static readonly Type ExternalIdentifier = typeof(UserId);

    [Fact]
    public void AggregateIdentifiers_AreStronglyTypedValueObjects()
    {
        Assert.NotEmpty(DeclaredProperties);

        // A raw Guid only as the Value of an identifier itself.
        var rawGuids = DeclaredProperties
            .Where(entry => Unwrap(entry.Property.PropertyType) == typeof(Guid))
            .Where(entry => !(entry.Property.Name == "Value"
                && (typeof(IEntityId).IsAssignableFrom(entry.Type) || entry.Type == ExternalIdentifier)))
            .Select(entry => $"{entry.Type.Name}.{entry.Property.Name}");
        Assert.Empty(rawGuids);

        // A string only as the Value of a name.
        var strings = DeclaredProperties
            .Where(entry => entry.Property.PropertyType == typeof(string))
            .Where(entry => !(entry.Property.Name == "Value" && entry.Type.Name.EndsWith("Name", StringComparison.Ordinal)))
            .Select(entry => $"{entry.Type.Name}.{entry.Property.Name}");
        Assert.Empty(strings);

        // Every identifier-typed property, the aggregates' Id included, is a readonly record struct
        // implementing IEntityId; UserId is the one external identifier.
        var identifierTypes = DeclaredProperties
            .Select(entry => Unwrap(entry.Property.PropertyType))
            .Concat(typeof(CompanyFounding).Assembly.GetExportedTypes()
                .Select(type => type.GetProperty("Id")?.PropertyType)
                .OfType<Type>())
            .Where(type => type.Name.EndsWith("Id", StringComparison.Ordinal) && type != ExternalIdentifier)
            .Distinct()
            .ToList();
        Assert.Equal(6, identifierTypes.Count);
        Assert.All(identifierTypes, type =>
        {
            Assert.True(type.IsValueType, $"{type.Name} is not a struct.");
            Assert.True(type.IsDefined(typeof(IsReadOnlyAttribute)), $"{type.Name} is not readonly.");
            Assert.NotNull(type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.True(typeof(IEntityId).IsAssignableFrom(type), $"{type.Name} does not implement IEntityId.");
        });
    }

    [Fact]
    public void AggregateIdentifiers_OrganizationIdIsTheReferenceGiven()
    {
        var reference = Founder.NewReference();

        var founded = Founder.Found(Founder.NewUser(), reference: reference);

        Assert.Equal(reference, founded.Organization.Id);
    }

    [Fact]
    public void AggregateIdentifiers_EveryOtherIdentifierIsAUuidV7SetByTheFounding()
    {
        var founded = Founder.Found(Founder.NewUser());

        Guid[] identifiers =
        [
            founded.Profile.Id.Value, founded.Workspace.Id.Value, founded.Ownership.Id.Value,
            founded.Membership.Id.Value, founded.Access.Id.Value,
        ];

        Assert.All(identifiers, identifier =>
        {
            Assert.Equal(7, identifier.Version);
            Assert.True(OrganizationId.IsVersion7(identifier));
            Assert.Equal(Founder.Now.ToUnixTimeMilliseconds(), UnixMilliseconds(identifier));
        });
        Assert.Equal(identifiers.Length + 1, identifiers.Append(founded.Organization.Id.Value).Distinct().Count());
    }

    [Fact]
    public void AggregateIdentifiers_OrganizationReferenceMustBeAUuidV7()
    {
        var version7 = Guid.CreateVersion7();
        var otherVariant = version7.ToByteArray(bigEndian: true);
        otherVariant[8] = (byte)(0xC0 | (otherVariant[8] & 0x1F));
        var version7OtherVariant = new Guid(otherVariant, bigEndian: true);
        Assert.Equal(7, version7OtherVariant.Version);

        Assert.True(OrganizationId.TryCreate(version7, out var reference));
        Assert.Equal(version7, reference.Value);

        foreach (var refused in new[] { Guid.NewGuid(), Guid.Empty, version7OtherVariant })
        {
            Assert.False(OrganizationId.TryCreate(refused, out var none), $"{refused} was accepted.");
            Assert.Equal(default, none);
        }
    }

    [Fact]
    public void AggregateIdentifiers_ConstructorsRefuseADefaultIdentifier()
    {
        var actor = new Actor("someone");
        var user = Founder.NewUser();
        var profile = ProfileId.New(Founder.Now);
        var organization = Founder.NewReference();

        Assert.Throws<ArgumentException>(() => new Profile(default, user, Founder.Now, actor));
        Assert.Throws<ArgumentException>(() => new Membership(default, profile, organization, Founder.Now, actor));
        Assert.Throws<ArgumentException>(() => new Membership(MembershipId.New(Founder.Now), default, organization, Founder.Now, actor));
        Assert.Throws<ArgumentException>(() => new Membership(MembershipId.New(Founder.Now), profile, default, Founder.Now, actor));
    }

    [Fact]
    public void AggregateIdentifiers_FoundingRefusesADefaultReference()
    {
        Assert.Throws<ArgumentException>(() => Founder.Found(Founder.NewUser(), reference: default(OrganizationId)));
    }

    [Fact]
    public void AggregateIdentifiers_FoundingRefusesAReferenceThatIsNotAUuidV7()
    {
        var otherVariant = Guid.CreateVersion7().ToByteArray(bigEndian: true);
        otherVariant[8] = (byte)(0xC0 | (otherVariant[8] & 0x1F));
        var version7OtherVariant = new Guid(otherVariant, bigEndian: true);
        Assert.Equal(7, version7OtherVariant.Version);

        foreach (var refused in new[] { Guid.NewGuid(), version7OtherVariant })
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                Founder.Found(Founder.NewUser(), reference: new OrganizationId(refused)));
            Assert.Equal("reference", exception.ParamName);
        }
    }

    private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static long UnixMilliseconds(Guid identifier)
    {
        var bytes = identifier.ToByteArray(bigEndian: true);
        return bytes.Take(6).Aggregate(0L, (milliseconds, octet) => (milliseconds << 8) | octet);
    }
}
