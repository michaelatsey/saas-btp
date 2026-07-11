namespace SaasBtp.Safety.UnitTests;

/// <summary>
/// The closed-set Value Objects (safety-domain-model.md §3): fixed membership, stable tokens, and a
/// total <c>FromToken</c> that resolves known tokens and rejects unknown/null ones.
/// </summary>
public sealed class ClosedSetTests
{
    [Fact]
    public void ConstatType_All_IsExactlyTheThreeMembers()
    {
        ConstatType.All.Select(type => type.Token)
            .ShouldBe(["incident", "accident", "dangerous_situation"]);
    }

    [Fact]
    public void Severity_All_IsExactlyTheThreeMembers()
    {
        Severity.All.Select(severity => severity.Token)
            .ShouldBe(["minor", "major", "critical"]);
    }

    [Theory]
    [InlineData("incident")]
    [InlineData("accident")]
    [InlineData("dangerous_situation")]
    public void ConstatType_FromToken_KnownToken_Succeeds(string token)
    {
        var result = ConstatType.FromToken(token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Token.ShouldBe(token);
    }

    [Theory]
    [InlineData("minor")]
    [InlineData("major")]
    [InlineData("critical")]
    public void Severity_FromToken_KnownToken_Succeeds(string token)
    {
        var result = Severity.FromToken(token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Token.ShouldBe(token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Incident")] // token is case-sensitive lowercase snake_case
    [InlineData("unknown")]
    [InlineData(null)]
    public void ConstatType_FromToken_UnknownOrNull_Fails(string? token)
    {
        var result = ConstatType.FromToken(token);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.Value.ShouldBe("SAFETY.CONSTAT.UNKNOWN_TYPE");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Critical")]
    [InlineData("blocker")]
    [InlineData(null)]
    public void Severity_FromToken_UnknownOrNull_Fails(string? token)
    {
        var result = Severity.FromToken(token);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.Value.ShouldBe("SAFETY.CONSTAT.UNKNOWN_SEVERITY");
    }
}
