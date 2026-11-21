using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Xunit;

namespace MicroKit.Domain.Tests.ValueObjects.Common;

public sealed class EmailTests
{
    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name@domain.co.uk")]
    [InlineData("firstname+lastname@company.org")]
    [InlineData("test123@test-domain.com")]
    public void Constructor_ValidEmail_ShouldCreateInstance(string email)
    {
        // Act
        var emailObj = new Email(email);

        // Assert
        Assert.Equal(email.ToLowerInvariant(), emailObj.Value);
    }

    [Theory]
    [InlineData("TEST@EXAMPLE.COM", "test@example.com")]
    [InlineData("User.Name@Domain.COM", "user.name@domain.com")]
    [InlineData("  test@example.com  ", "test@example.com")]
    public void Constructor_EmailNormalization_ShouldNormalizeEmail(string input, string expected)
    {
        // Act
        var email = new Email(input);

        // Assert
        Assert.Equal(expected, email.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_InvalidEmail_ShouldThrowDomainException(string? email)
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => new Email(email!));
        Assert.Equal("Email address cannot be null or empty.", ex.Message);
    }

    [Theory]
    [InlineData("victim@example.com\r\nBcc: attacker@example.org")]
    [InlineData("victim@example.com\nBcc:attacker@example.org")]
    [InlineData("john doe@example.com")]
    [InlineData("john@exam ple.com")]
    [InlineData("john\t@example.com")]
    [InlineData("john\u0000@example.com")]
    [InlineData("john\u00A0doe@example.com")]
    public void Constructor_WhiteSpaceOrControlCharacterInside_ThrowsDomainException(string email)
    {
        // A line break would let an address inject a header into a message built from it.
        Assert.Equal("Email address cannot contain white space or control characters.", Assert.Throws<DomainException>(() => new Email(email))
            .Message);
    }

    [Theory]
    [InlineData("victim@example.com,attacker@example.org")]   // a header would read a recipient list
    [InlineData("a@b.c,evil")]
    [InlineData("a;b@example.com")]
    [InlineData("a<b>@example.com")]
    [InlineData("\"john\"@example.com")]
    [InlineData("admin\u200B@example.com")]                    // zero-width space, invisible
    [InlineData("admin@exam\u202Eple.com")]                    // right-to-left override
    [InlineData("a@exa_mple.com")]
    [InlineData("a@[192.168.0.1]")]
    public void Constructor_CharacterOutsideTheAllowedSets_ThrowsDomainException(string email)
    {
        Assert.Throws<DomainException>(() => new Email(email));
    }

    [Theory]
    [InlineData("a;b@example.com", "Email local part can contain only letters, digits, combining marks and the characters ! # $ % & ' * + - / = ? ^ _ ` { | } ~ .")]
    [InlineData("a@exa_mple.com", "Email domain can contain only letters, digits, combining marks, '-' and '.'.")]
    public void Constructor_CharacterOutsideTheAllowedSets_NamesWhatIsAllowed(string email, string message)
    {
        Assert.Equal(message, Assert.Throws<DomainException>(() => new Email(email)).Message);
    }

    [Fact]
    public void Constructor_DecomposedAccent_IsNormalizedToNfcAndEqualsThePrecomposedForm()
    {
        var decomposed = new Email("jose\u0301@exemple.fr");

        Assert.Equal("jos\u00E9@exemple.fr", decomposed.Value);
        Assert.Equal(new Email("jos\u00E9@exemple.fr"), decomposed);
    }

    [Fact]
    public void Constructor_LetterOutsideTheBmp_Succeeds()
    {
        // U+20000, a CJK Extension B ideograph, encoded as a surrogate pair. Built in code because
        // xUnit's theory-data serialization is not reliable for surrogates.
        var email = char.ConvertFromUtf32(0x20000) + "@example.com";

        Assert.Equal(email, new Email(email).Value);
    }

    [Theory]
    [InlineData("admin\uFE0F@example.com")]   // variation selector, invisible
    [InlineData("ad\u034Fmin@example.com")]   // combining grapheme joiner, invisible
    [InlineData("a\u180Fb@example.com")]      // Mongolian free variation selector four
    [InlineData("a\u3164b@example.com")]      // Hangul filler, an invisible letter (Lo)
    [InlineData("a\u17B4b@example.com")]      // Khmer inherent vowel, invisible (Mn)
    [InlineData("a\u00ADb@example.com")]      // soft hyphen
    [InlineData("\u0645\u06CC\u200C\u062E\u0648\u0627\u0645@example.com")]   // ZWNJ, a documented limit
    public void Constructor_InvisibleVariantSelector_ThrowsDomainException(string email)
    {
        Assert.Throws<DomainException>(() => new Email(email));
    }

    [Fact]
    public void Constructor_UnpairedSurrogate_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => new Email("ab" + '\uD800' + "@example.com"));
    }

    [Theory]
    [InlineData("o'brien@example.com")]
    [InlineData("a!#$%&*/=?^_`{|}~-z@example.com")]
    [InlineData("jos\u00E9@exemple.fr")]
    [InlineData("user@b\u00FCcher.de")]
    [InlineData("\u0928\u092E\u0938\u094D\u0924\u0947@example.com")]              // Devanagari, Mc and Mn signs
    [InlineData("user@\u092E\u0930\u093E\u0920\u0940.\u092D\u093E\u0930\u0924")]  // Devanagari domain
    public void Constructor_AllowedCharacters_Succeed(string email)
    {
        Assert.Equal(email.ToLowerInvariant(), new Email(email).Value);
    }

    [Fact]
    public void ImplicitConversionToString_NullEmail_ReturnsNull()
    {
        // Declared non-null on purpose: the conversion must cope with a null that slipped through.
        Email email = null!;

        string? value = email;

        Assert.Null(value);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("@")]
    [InlineData("ab")]
    public void Constructor_TooShortEmail_ShouldThrowDomainException(string email)
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => new Email(email));
        Assert.Equal("Email address is too short.", ex.Message);
    }

    [Fact]
    public void Constructor_TooLongEmail_ShouldThrowDomainException()
    {
        // Arrange - Create an email longer than 254 characters (but local part under 64 limit)
        var localPart = new string('a', 50); // 50 characters - under 64 limit
        var domainPart = new string('b', 200) + ".com"; // 204 characters - under 253 limit
        var longEmail = $"{localPart}@{domainPart}"; // 50 + 1 + 204 = 255 characters > 254

        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => new Email(longEmail));
        Assert.Equal("Email address is too long.", ex.Message);
    }

    [Theory]
    [InlineData("testexample.com")]        // Missing @
    [InlineData("test@@example.com")]      // Multiple @
    [InlineData("test@")]                  // Missing domain
    [InlineData("@example.com")]           // Missing local part
    public void Constructor_InvalidFormat_ShouldThrowDomainException(string email)
    {
        // Act & Assert
        Assert.Throws<DomainException>(() => new Email(email));
    }

    [Theory]
    [InlineData("test@example")]           // Domain missing dot
    [InlineData("test@")]                  // Empty domain
    public void Constructor_InvalidDomain_ShouldThrowDomainException(string email)
    {
        // Act & Assert
        Assert.Throws<DomainException>(() => new Email(email));
    }

    [Theory]
    [InlineData(".test@example.com")]      // Local part starts with dot
    [InlineData("test.@example.com")]      // Local part ends with dot
    [InlineData("te..st@example.com")]     // Consecutive dots in local part
    public void Constructor_InvalidLocalPartDots_ShouldThrowDomainException(string email)
    {
        // Act & Assert
        Assert.Throws<DomainException>(() => new Email(email));
    }

    [Theory]
    [InlineData("test@.example.com")]      // Domain starts with dot
    [InlineData("test@example.com.")]      // Domain ends with dot
    [InlineData("test@exam..ple.com")]     // Consecutive dots in domain
    public void Constructor_InvalidDomainDots_ShouldThrowDomainException(string email)
    {
        // Act & Assert
        Assert.Throws<DomainException>(() => new Email(email));
    }

    [Fact]
    public void Constructor_LocalPartTooLong_ShouldThrowDomainException()
    {
        // Arrange - Create local part longer than 64 characters
        var longLocalPart = new string('a', 65);
        var email = $"{longLocalPart}@example.com";

        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => new Email(email));
        Assert.Equal("Email local part length is invalid.", ex.Message);
    }

    [Fact]
    public void Constructor_DomainPartTooLong_ShouldThrowDomainException()
    {
        // Arrange - Create domain part longer than 253 characters
        var longDomain = new string('a', 252) + ".com"; // 252 + 4 = 256 characters
        var email = $"test@{longDomain}";

        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => new Email(email));
        Assert.Equal("Email domain part length is invalid.", ex.Message);
    }

    [Fact]
    public void ImplicitConversionFromString_ShouldWork()
    {
        // Arrange
        const string emailString = "test@example.com";

        // Act
        Email email = emailString;

        // Assert
        Assert.Equal(emailString, email.Value);
    }

    [Fact]
    public void ImplicitConversionToString_ShouldWork()
    {
        // Arrange
        var email = new Email("test@example.com");

        // Act
        string emailString = email;

        // Assert
        Assert.Equal("test@example.com", emailString);
    }

    [Fact]
    public void ToString_ShouldReturnEmailValue()
    {
        // Arrange
        var email = new Email("test@example.com");

        // Act
        var result = email.ToString();

        // Assert
        Assert.Equal("test@example.com", result);
    }

    [Theory]
    [InlineData("test@example.com", "test@example.com", true)]
    [InlineData("test@example.com", "TEST@EXAMPLE.COM", true)]    // Case insensitive
    [InlineData("test@example.com", "test@other.com", false)]
    public void Equality_ShouldWorkCorrectly(string email1, string email2, bool expected)
    {
        // Arrange
        var emailObj1 = new Email(email1);
        var emailObj2 = new Email(email2);

        // Act & Assert
        Assert.Equal(expected, emailObj1.Equals(emailObj2));
        Assert.Equal(expected, (emailObj1 == emailObj2));
        Assert.Equal(!expected, (emailObj1 != emailObj2));
    }

    [Fact]
    public void GetHashCode_CaseInsensitiveEmails_ShouldHaveSameHashCode()
    {
        // Arrange
        var email1 = new Email("test@example.com");
        var email2 = new Email("TEST@EXAMPLE.COM");

        // Act & Assert
        Assert.Equal(email2.GetHashCode(), email1.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentEmails_ShouldHaveDifferentHashCodes()
    {
        // Arrange
        var email1 = new Email("test@example.com");
        var email2 = new Email("test@other.com");

        // Act & Assert
        Assert.NotEqual(email2.GetHashCode(), email1.GetHashCode());
    }

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name+tag@domain.co.uk")]
    [InlineData("firstname-lastname@company.org")]
    public void Constructor_CommonValidFormats_ShouldSucceed(string email)
    {
        // Act & Assert
        Assert.Null(Record.Exception(() => new Email(email)));
    }
}
