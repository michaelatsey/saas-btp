using System.Reflection;
using System.Text;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Domain.Tests;

/// <summary>
/// The measure of the company name and of the first workspace name: BP001-R7, as contract.md §2
/// states it (<c>string.Trim()</c>, then a count in Unicode scalar values, the trimmed value
/// otherwise unchanged and not normalized).
/// </summary>
public sealed class NameTests
{
    public const string Organization = "organization";
    public const string Workspace = "workspace";

    public static readonly TheoryData<string> Kinds = new(Organization, Workspace);

    public static readonly TheoryData<string, string?> MissingNames = Cross(null, "", " ", "   ", "\t", "\u00A0", "\t\u00A0 \r\n");

    public static readonly TheoryData<string, string> ZeroWidthAlone = Cross("\u200B", "\uFEFF");

    [Theory]
    [MemberData(nameof(MissingNames))]
    public void BP001_R7_NameIsMissingWhenEmptyOnceTrimmed(string kind, string? declared)
    {
        var (value, defect) = Measure(kind, declared);

        Assert.Null(value);
        Assert.Equal(NameDefect.Missing, defect);
    }

    [Theory]
    [MemberData(nameof(ZeroWidthAlone))]
    public void BP001_R7_ZeroWidthCharacterAloneIsNotMissing(string kind, string declared)
    {
        var (value, defect) = Measure(kind, declared);

        Assert.Equal(NameDefect.None, defect);
        Assert.Equal(declared, value);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BP001_R7_WhiteSpaceAroundIsRemovedAndZeroWidthCharactersAreKept(string kind)
    {
        var (value, defect) = Measure(kind, "\t\u00A0\u200BKouassi  BTP\uFEFF\u00A0\t");

        Assert.Equal(NameDefect.None, defect);
        Assert.Equal("\u200BKouassi  BTP\uFEFF", value);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BP001_R7_NameAtTheLimitOnceTrimmedIsKept(string kind)
    {
        var name = new string('a', 200);

        var (value, defect) = Measure(kind, "\t\u00A0 " + name + " \u00A0\t");

        Assert.Equal(NameDefect.None, defect);
        Assert.Equal(name, value);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BP001_R7_NameOneOverTheLimitIsTooLong(string kind)
    {
        var (value, defect) = Measure(kind, new string('a', 201));

        Assert.Null(value);
        Assert.Equal(NameDefect.TooLong, defect);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BP001_R7_ZeroWidthPaddingCountsTowardTheLimit(string kind)
    {
        var (value, defect) = Measure(kind, "\u200B" + new string('a', 199) + "\uFEFF");

        Assert.Null(value);
        Assert.Equal(NameDefect.TooLong, defect);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BP001_R7_CharactersOfTwoUtf16CodeUnitsCountOnceEach(string kind)
    {
        var name = string.Concat(Enumerable.Repeat("\U0001F600", 200));
        Assert.Equal(400, name.Length);

        var (value, defect) = Measure(kind, name);
        var (_, over) = Measure(kind, name + "\U0001F600");

        Assert.Equal(NameDefect.None, defect);
        Assert.Equal(name, value);
        Assert.Equal(NameDefect.TooLong, over);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BP001_R7_LettersWithASeparateAccentCountTwiceEach(string kind)
    {
        var name = string.Concat(Enumerable.Repeat("e\u0301", 101));
        Assert.Equal(101, new System.Globalization.StringInfo(name).LengthInTextElements);

        var (value, defect) = Measure(kind, name);

        Assert.Null(value);
        Assert.Equal(NameDefect.TooLong, defect);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BP001_R7_NameIsNotNormalized(string kind)
    {
        var decomposed = "Se\u0301ge".Normalize(NormalizationForm.FormD);
        Assert.NotEqual(decomposed, decomposed.Normalize(NormalizationForm.FormC));

        var (value, _) = Measure(kind, decomposed);

        Assert.Equal(decomposed, value);
    }

    [Theory]
    [InlineData(typeof(OrganizationName))]
    [InlineData(typeof(WorkspaceName))]
    public void BP001_R7_NameIsBuiltOnlyThroughItsMeasure(Type name)
    {
        Assert.Empty(name.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(
            name.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.False(Members.IsSettableOutside(property), $"{name.Name}.{property.Name} can be set from outside the type."));
        Assert.All(
            name.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void BP001_R7_LimitIs200()
    {
        Assert.Equal(200, OrganizationName.MaxLength);
        Assert.Equal(200, WorkspaceName.MaxLength);
    }

    private static (string? Value, NameDefect Defect) Measure(string kind, string? declared) => kind switch
    {
        Organization => OrganizationName.TryCreate(declared, out var name, out var defect) ? (name.Value, defect) : (null, defect),
        Workspace => WorkspaceName.TryCreate(declared, out var name, out var defect) ? (name.Value, defect) : (null, defect),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown name."),
    };

    private static TheoryData<string, T> Cross<T>(params T[] values)
    {
        var data = new TheoryData<string, T>();
        foreach (var kind in new[] { Organization, Workspace })
        {
            foreach (var value in values)
                data.Add(kind, value);
        }

        return data;
    }
}
