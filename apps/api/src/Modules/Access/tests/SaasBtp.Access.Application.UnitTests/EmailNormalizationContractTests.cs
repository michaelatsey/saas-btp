using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace SaasBtp.Access.Application.UnitTests;

/// <summary>
/// A CONTRACT test on a dependency we rely on for a security-relevant invariant.
/// </summary>
/// <remarks>
/// ADR-ARCH-011 makes e-mail normalization (lowercase, trimmed) a load-bearing rule: the partial
/// unique index that stops invitee-driven privilege escalation compares RAW strings, so it only
/// protects data that already honours the normalization contract. That ADR is explicit — "an
/// untested invariant is a comment".
/// <para>
/// The founding flow delegates that invariant to MicroKit's <see cref="Email"/> value object instead
/// of a hand-rolled helper. These tests pin the behaviour we depend on, so a MicroKit upgrade that
/// silently changed it would fail here rather than in production.
/// </para>
/// </remarks>
public sealed class EmailNormalizationContractTests
{
    [Theory]
    [InlineData("Paul@Mail.com", "paul@mail.com")]
    [InlineData("  paul@mail.com  ", "paul@mail.com")]
    [InlineData(" PAUL@MAIL.COM ", "paul@mail.com")]
    public void Email_normalizes_to_lowercase_and_trimmed(string raw, string expected)
        => new Email(raw).Value.ShouldBe(expected);

    [Fact]
    public void Two_textual_forms_of_the_same_address_are_the_same_value()
        => new Email(" Paul@Mail.com ").ShouldBe(new Email("paul@mail.com"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public void A_malformed_address_is_refused(string malformed)
        => Should.Throw<Exception>(() => new Email(malformed));
}
