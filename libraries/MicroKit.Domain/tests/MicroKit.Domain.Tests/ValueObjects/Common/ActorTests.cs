using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Xunit;

namespace MicroKit.Domain.Tests.ValueObjects.Common;

public sealed class ActorTests
{
    [Theory]
    [InlineData("$system")]
    [InlineData("$anything")]
    [InlineData("  $system")]
    public void Constructor_ReservedPrefix_ThrowsDomainException(string value)
    {
        Assert.Throws<DomainException>(() => new Actor(value));
    }

    [Fact]
    public void System_IsNotEqualToAUserNamedSystem()
    {
        Assert.True((Actor.System != new Actor("system")));
        Assert.False(Actor.System.Equals(new Actor("system")));
    }

    [Fact]
    public void BuiltIns_CarryTheReservedPrefix()
    {
        Assert.Equal("$system", Actor.System.Value);
        Assert.Equal("$anonymous", Actor.Anonymous.Value);
        Assert.Equal("$migration", Actor.Migration.Value);
    }

    [Fact]
    public void BuiltIns_AreDistinct()
    {
        Actor[] builtIns = [Actor.System, Actor.Anonymous, Actor.Migration];

        Assert.Equal(3, builtIns.Distinct().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_Empty_ThrowsDomainException(string? value)
    {
        Assert.Throws<DomainException>(() => new Actor(value!));
    }

    [Fact]
    public void Constructor_TooLong_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => new Actor(new string('a', Actor.MaxLength + 1)));
    }

    [Fact]
    public void Constructor_MaxLength_IsAccepted()
    {
        Assert.Equal(Actor.MaxLength, new Actor(new string('a', Actor.MaxLength)).Value.Length);
    }

    [Fact]
    public void Constructor_TrimsAndKeepsAnInnerDollar()
    {
        var actor = new Actor("  user$1  ");

        Assert.Equal("user$1", actor.Value);
        Assert.Equal("user$1", actor.ToString());
    }

    [Fact]
    public void Restore_BuiltInValues_ReturnTheBuiltIns()
    {
        Assert.Same(Actor.System, Actor.Restore(Actor.System.Value));
        Assert.Same(Actor.Anonymous, Actor.Restore(Actor.Anonymous.Value));
        Assert.Same(Actor.Migration, Actor.Restore(Actor.Migration.Value));
    }

    [Theory]
    [InlineData("$system   ")]
    [InlineData("  $system")]
    [InlineData("\t$system\r\n")]
    public void Restore_PaddedBuiltInValue_ReturnsTheBuiltIn(string stored)
    {
        // A fixed-width column pads the stored value; the constructor trims a user value the same way.
        Assert.Same(Actor.System, Actor.Restore(stored));
    }

    [Theory]
    [InlineData("alice\nINFO forged audit line")]
    [InlineData("alice\r\nbob")]
    [InlineData("alice\tbob")]
    [InlineData("alice\u0000")]
    [InlineData("ali\u200Bce")]   // zero-width space
    [InlineData("alice\u202E")]   // right-to-left override
    [InlineData("alice\u2028INFO forged")]   // line separator, a line break in log viewers
    [InlineData("alice\u2029INFO forged")]   // paragraph separator
    [InlineData("alice\uFE0F")]              // variation selector, default-ignorable
    [InlineData("ali\u3164ce")]              // Hangul filler, an invisible letter
    [InlineData("alice\u00AD")]              // soft hyphen
    [InlineData("alice\u0378")]              // unassigned code point
    public void Constructor_ControlOrFormatCharacter_ThrowsDomainException(string value)
    {
        Assert.Equal("Actor identifier cannot contain an invisible, control or line-breaking character.", Assert.Throws<DomainException>(() => new Actor(value))
            .Message);
        Assert.Throws<DomainException>(() => Actor.Restore(value));
    }

    // Facts, not theory data: xUnit serializes theory data at discovery and replaces an unpaired
    // surrogate with U+FFFD, so the value would never reach the test.
    [Fact]
    public void Constructor_UnpairedSurrogate_ThrowsDomainException()
    {
        foreach (var value in new[] { "alice" + '\uD800', '\uDC00' + "alice" })
            Assert.Equal("Actor identifier cannot contain an invisible, control or line-breaking character.", Assert.Throws<DomainException>(() => new Actor(value))
                .Message);
    }

    [Fact]
    public void Constructor_FormatCharacterOutsideTheBmp_ThrowsDomainException()
    {
        // U+E0041 TAG LATIN CAPITAL LETTER A, an invisible Cf encoded as a surrogate pair.
        Assert.Equal("Actor identifier cannot contain an invisible, control or line-breaking character.", Assert.Throws<DomainException>(() => new Actor("alice" + char.ConvertFromUtf32(0xE0041)))
            .Message);
    }

    [Theory]
    [InlineData("service account")]
    [InlineData("auth0|5f7c8ec7c33c6c004bbafe82")]
    [InlineData("Jos\u00E9")]
    [InlineData("user-\uD83D\uDE00")]       // a surrogate pair is one printable character
    public void Constructor_PrintableIdentifier_IsAccepted(string value)
    {
        Assert.Equal(value, new Actor(value).Value);
    }

    [Fact]
    public void Restore_Null_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => Actor.Restore(null!));
    }

    [Fact]
    public void Restore_PaddedUserValue_ReturnsTheTrimmedActor()
    {
        Assert.Equal(new Actor("alice"), Actor.Restore("alice   "));
    }

    [Fact]
    public void Restore_UserValue_RoundTrips()
    {
        var actor = new Actor("user-1");

        Assert.Equal(actor, Actor.Restore(actor.Value));
        Assert.NotEqual(Actor.System, Actor.Restore("system"));
    }

    [Theory]
    [InlineData("$other")]
    [InlineData("$System")]
    [InlineData("")]
    public void Restore_UnknownReservedOrEmptyValue_ThrowsDomainException(string value)
    {
        Assert.Throws<DomainException>(() => Actor.Restore(value));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(new Actor(" user-1 "), new Actor("user-1"));
    }
}
