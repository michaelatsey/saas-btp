using System.Xml.Linq;

namespace MicroKit.Domain.Architecture.Tests;

// The XML documentation file ships in the package and is what IntelliSense shows. These tests
// pin the parts of it that answer a known adopter question.
public sealed class PublishedDocumentationTests
{
    private static readonly XDocument Documentation =
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MicroKit.Domain.xml"));

    [Fact]
    public void EventRoot_DoesNotPresentARejectedContract()
    {
        var doc = Member("T:MicroKit.Domain.Events.IEvent");

        Assert.Contains("IDomainEvent", doc);
        Assert.Contains("IIntegrationEvent", doc);
        Assert.DoesNotContain("IApplicationEvent", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntityConstructor_ExplainsEfCoreConstructorBinding()
    {
        var doc = Member("M:MicroKit.Domain.Aggregates.Entity`1.#ctor(`0)");

        Assert.Contains("no parameterless constructor", doc);
        Assert.Contains("EF Core calls a parameterised constructor", doc);
        Assert.Contains("camelCase parameter binding a PascalCase property", doc);
    }

    [Theory]
    [InlineData("AuditableEntity`1")]
    [InlineData("AuditableAggregateRoot`1")]
    public void AuditableConstructor_ExplainsHowToMaterialiseAnActor(string type)
    {
        var doc = Member(
            $"M:MicroKit.Domain.Aggregates.{type}.#ctor(`0,System.DateTimeOffset,MicroKit.Domain.ValueObjects.Common.Actor)");

        Assert.Contains("binds constructor parameters to mapped properties by name", doc);
        Assert.Contains("owned or complex type", doc);
        Assert.Contains("value converter", doc);
        Assert.Contains("private constructor for materialisation", doc);
    }

    private const string AggregateMutate =
        "M:MicroKit.Domain.Aggregates.AuditableAggregateRoot`1.Mutate(System.DateTimeOffset,MicroKit.Domain.ValueObjects.Common.Actor,System.Action)";

    [Fact]
    public void AggregateMutate_StatesTheEventRemovalInOneParagraph()
    {
        // Structure, not wording: the general paragraph says nothing of events, and exactly one
        // paragraph of the remarks is about the events of a failed mutation.
        var paragraphs = Documentation.Descendants("member")
            .Single(m => (string?)m.Attribute("name") == AggregateMutate)
            .Element("remarks")!
            .Elements("para")
            .Select(p => p.Value)
            .ToArray();

        Assert.DoesNotContain("event", paragraphs[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, paragraphs.Count(p => p.Contains("domain events", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("AuditableEntity`1")]
    [InlineData("AuditableAggregateRoot`1")]
    public void Mutate_StatesThatAFailedMutationKeepsChangesToOtherObjects(string type)
    {
        var doc = Member(
            $"M:MicroKit.Domain.Aggregates.{type}.Mutate(System.DateTimeOffset,MicroKit.Domain.ValueObjects.Common.Actor,System.Action)");

        Assert.Contains("on this object and on any other", doc);
        Assert.Contains("keeps its audit stamp", doc);
        Assert.Contains("before mutating any child", doc);
    }

    // MicroKit ADR-004 D1 (michaelatsey/microkit@a12097b): a cache reads back what it wrote, so it is not a path that only writes.
    [Theory]
    [InlineData("T:MicroKit.Domain.ValueObjects.Common.Actor")]
    [InlineData("T:MicroKit.Domain.ValueObjects.Common.Json.ActorJsonConverter")]
    public void ActorJson_DoesNotPresentACacheAsAWriteOnlyPath(string type)
    {
        var doc = Member(type);

        Assert.DoesNotContain("or a cache written with default options never fails", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only writes, such as an HTTP response or a log", doc);
        Assert.Contains("a cache", doc);
        Assert.Contains("reads with", doc);
    }

    // MicroKit ADR-004 D9 (michaelatsey/microkit@a12097b): an OIDC sub may be longer than MaxLength; the documentation must not claim it fits.
    [Theory]
    [InlineData("T:MicroKit.Domain.ValueObjects.Common.Actor")]
    [InlineData("F:MicroKit.Domain.ValueObjects.Common.Actor.MaxLength")]
    public void Actor_StatesTheOidcSubjectLengthLimit(string member)
    {
        var doc = Member(member);

        Assert.Contains("255", doc);
        Assert.Contains("ADR-004 D9", doc);
    }

    [Fact]
    public void UtcInstant_StatesWhichInstantsItApplies()
    {
        // DateRange keeps its offset, so the rule is not applied to "every instant".
        var doc = Member("T:MicroKit.Domain.UtcInstant");

        Assert.DoesNotContain("every instant entering the domain", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("audit and event instants", doc);
        Assert.Contains("DateRange", doc);
    }

    [Theory]
    [InlineData("M:MicroKit.Domain.ValueObjects.Common.Money.Add(MicroKit.Domain.ValueObjects.Common.Money)")]
    [InlineData("M:MicroKit.Domain.ValueObjects.Common.Money.Subtract(MicroKit.Domain.ValueObjects.Common.Money)")]
    [InlineData("M:MicroKit.Domain.ValueObjects.Common.Money.Multiply(System.Decimal,System.MidpointRounding)")]
    [InlineData("M:MicroKit.Domain.ValueObjects.Common.Money.Allocate(System.Int32[])")]
    public void MoneyArithmetic_DocumentsItsOverflow(string member)
    {
        Assert.Contains("<exception cref=\"T:System.OverflowException\">", Member(member));
    }

    [Fact]
    public void PhoneNumber_StatesThatATrunkPrefixIsKept()
    {
        // The example must not present a number with its trunk 0 after the country code as the norm.
        var doc = Member("T:MicroKit.Domain.ValueObjects.Common.PhoneNumber");

        Assert.Contains("no national trunk prefix is removed", doc);
        Assert.Contains("becomes <c>+33612345678x12</c>", doc);
    }

    // MicroKit ADR-004 D5, D8 (michaelatsey/microkit@a12097b): the text rules state what they do not check, so they promise nothing more.
    [Theory]
    [InlineData("T:MicroKit.Domain.UnicodeText", "not a spoofing detector")]
    [InlineData("T:MicroKit.Domain.UnicodeText", "should run the same .NET version")]
    [InlineData("T:MicroKit.Domain.ValueObjects.Common.Actor", "Identifiers are compared ordinally")]
    [InlineData("T:MicroKit.Domain.ValueObjects.Common.Email", "not full Unicode case folding")]
    [InlineData("T:MicroKit.Domain.ValueObjects.Common.Email", "count UTF-16 code units")]
    [InlineData("T:MicroKit.Domain.ValueObjects.Common.Email", "zero-width joiner and non-joiner")]
    public void TextRules_StateTheirLimits(string member, string limit)
    {
        Assert.Contains(limit, Member(member));
    }

    [Theory]
    [InlineData("M:MicroKit.Domain.ValueObjects.Common.Email.op_Implicit(System.String)~MicroKit.Domain.ValueObjects.Common.Email")]
    [InlineData("M:MicroKit.Domain.ValueObjects.Common.PhoneNumber.op_Implicit(System.String)~MicroKit.Domain.ValueObjects.Common.PhoneNumber")]
    public void ConversionFromString_DocumentsItsDomainException(string member)
    {
        Assert.Contains("<exception cref=\"T:MicroKit.Domain.Exceptions.DomainException\">", Member(member));
    }

    [Fact]
    public void Email_Summary_DoesNotPromiseCaseInsensitiveEquality()
    {
        // Equality is invariant lowercasing, not case folding (MicroKit ADR-004 D5 (michaelatsey/microkit@a12097b)).
        var doc = Member("T:MicroKit.Domain.ValueObjects.Common.Email");

        Assert.DoesNotContain("Provides case-insensitive equality", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lowercased with the invariant culture", doc);
    }

    [Fact]
    public void Actor_DoesNotPromiseThatAnActorCannotPassForAnother()
    {
        Assert.DoesNotContain("nor pass for another one", Member("T:MicroKit.Domain.ValueObjects.Common.Actor"), StringComparison.OrdinalIgnoreCase);
    }

    // #182, MicroKit ADR-004 D3 (michaelatsey/microkit@a12097b): a plain aggregate keeps the events and changes of a method that throws.
    [Theory]
    [InlineData("T:MicroKit.Domain.Aggregates.AggregateRoot`1")]
    [InlineData("M:MicroKit.Domain.Aggregates.AggregateRoot`1.RaiseDomainEvent(MicroKit.Domain.Events.IDomainEvent)")]
    [InlineData("M:MicroKit.Domain.Aggregates.AuditableEntity`1.Mutate(System.DateTimeOffset,MicroKit.Domain.ValueObjects.Common.Actor,System.Action)")]
    [InlineData(AggregateMutate)]
    public void AggregateMembers_StateThatAnInstanceIsDiscardedAfterAThrow(string member)
    {
        var doc = Member(member);

        Assert.Contains("must be discarded, not saved", doc);
        Assert.Contains("ADR-004 D3", doc);
    }

    [Fact]
    public void AggregateRoot_StatesThatOnlyMutateRemovesTheEventsOfAFailedOperation()
    {
        var doc = Member("T:MicroKit.Domain.Aggregates.AggregateRoot`1");

        Assert.Contains("does not roll back the domain events it raised", doc);
        Assert.Contains("AuditableAggregateRoot`1", doc);
        Assert.Contains("Discard the change set that tracks it too", doc);
    }

    // #182, MicroKit ADR-004 D2 (michaelatsey/microkit@a12097b): the closure and delegate a capturing Mutate lambda allocates.
    [Theory]
    [InlineData("AuditableEntity`1")]
    [InlineData("AuditableAggregateRoot`1")]
    public void Mutate_StatesItsAllocation(string type)
    {
        var doc = Member(
            $"M:MicroKit.Domain.Aggregates.{type}.Mutate(System.DateTimeOffset,MicroKit.Domain.ValueObjects.Common.Actor,System.Action)");

        Assert.Contains("allocates a closure and a delegate on each call", doc);
        Assert.Contains("ADR-004 D2", doc);
    }

    private static string Member(string name)
    {
        var member = Documentation.Descendants("member")
            .SingleOrDefault(m => (string?)m.Attribute("name") == name);

        Assert.NotNull(member);

        // The raw XML keeps cref targets; normalise the line breaks and indentation of the comment.
        return string.Join(' ', member.ToString().Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
    }
}
