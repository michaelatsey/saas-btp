using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Features.Organizations.Found;

namespace SaasBtp.Access.Features.Tests;

/// <summary>
/// The founding's handler against fake ports (contract.md §2, §7 "Features"): the body checked
/// before the level, each refusal naming only the field at fault, and nothing written on any refusal.
/// </summary>
public sealed class FoundCompanyHandlerTests
{
    private const string Valid = "Kouassi BTP";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The two names BP001-R7 measures, by the field the body gives each.</summary>
    public static TheoryData<string> Names => [FoundCompanyField.OrganizationName, FoundCompanyField.WorkspaceName];

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    [InlineData(null)]
    public async Task BP001_AC_07_AnEmptyCompanyNameIsAnswered422NamingOrganizationName(string? organizationName)
    {
        var founding = new FakeFoundingUnitOfWork();

        var outcome = await Handler(FakeCaller.Confirmed(), founding).HandleAsync(Reference(), organizationName, Valid, Token);

        Assert.Equal([FoundCompanyField.OrganizationName], Assert.IsType<FoundCompanyOutcome.Invalid>(outcome).Fields);
        Assert.Empty(founding.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task BP001_AC_08_AnEmptyWorkspaceNameIsAnswered422NamingWorkspaceName(string workspaceName)
    {
        var founding = new FakeFoundingUnitOfWork();

        var outcome = await Handler(FakeCaller.Confirmed(), founding).HandleAsync(Reference(), Valid, workspaceName, Token);

        Assert.Equal([FoundCompanyField.WorkspaceName], Assert.IsType<FoundCompanyOutcome.Invalid>(outcome).Fields);
        Assert.Empty(founding.Calls);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task BP001_AC_23_ANameOneCharacterOverTheLimitIsRefused(string field)
    {
        await AssertRefusedAsync(field, new string('a', OrganizationName.MaxLength + 1));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task BP001_AC_23_ANameAtTheLimitOnceTrimmedIsFoundedTrimmed(string field)
    {
        var atTheLimit = new string('a', OrganizationName.MaxLength);

        await AssertFoundedAsync(field, "  " + atTheLimit + "  ", atTheLimit);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task BP001_AC_23_TheLimitReachedWithCharactersOfTwoCodeUnitsIsFounded(string field)
    {
        // U+1D11E MUSICAL SYMBOL G CLEF: one Unicode scalar value, two UTF-16 code units.
        var clefs = string.Concat(Enumerable.Repeat("\U0001D11E", OrganizationName.MaxLength));
        Assert.Equal(2 * OrganizationName.MaxLength, clefs.Length);

        await AssertFoundedAsync(field, clefs, clefs);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task BP001_AC_23_ANameOverTheLimitInScalarValuesButWithinItInTextElementsIsRefused(string field)
    {
        // 101 letters, each written as "e" followed by U+0301 COMBINING ACUTE ACCENT: 202 scalar
        // values, 101 text elements.
        var accented = string.Concat(Enumerable.Repeat("é", (OrganizationName.MaxLength / 2) + 1));
        Assert.Equal((OrganizationName.MaxLength / 2) + 1, new System.Globalization.StringInfo(accented).LengthInTextElements);

        await AssertRefusedAsync(field, accented);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task BP001_AC_23_TabAndNoBreakSpaceAreRemovedAndZeroWidthSpaceIsKeptAndCounted(string field)
    {
        // Tab and U+00A0 NO-BREAK SPACE are white space; U+200B ZERO WIDTH SPACE is not.
        var kept = "​" + new string('a', OrganizationName.MaxLength - 1);
        await AssertFoundedAsync(field, "\t " + kept + " \t", kept);

        await AssertRefusedAsync(field, "\t ​" + new string('a', OrganizationName.MaxLength) + " \t");
    }

    [Theory]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ANonVersion7ReferenceIsAnswered422NamingOrganizationId(string reference)
    {
        var founding = new FakeFoundingUnitOfWork();

        var outcome = await Handler(FakeCaller.Confirmed(), founding).HandleAsync(Guid.Parse(reference), Valid, Valid, Token);

        Assert.Equal([FoundCompanyField.OrganizationId], Assert.IsType<FoundCompanyOutcome.Invalid>(outcome).Fields);
        Assert.Empty(founding.Calls);
    }

    [Fact]
    public async Task EveryFieldAtFaultIsNamedInTheOrderOfTheBody()
    {
        var founding = new FakeFoundingUnitOfWork();

        var outcome = await Handler(FakeCaller.Confirmed(), founding).HandleAsync(Guid.NewGuid(), " ", "", Token);

        Assert.Equal(
            [FoundCompanyField.OrganizationId, FoundCompanyField.OrganizationName, FoundCompanyField.WorkspaceName],
            Assert.IsType<FoundCompanyOutcome.Invalid>(outcome).Fields);
        Assert.Empty(founding.Calls);
    }

    [Fact]
    public async Task ACallerWithoutTheLevelIsAnswered403AfterTheBody()
    {
        var founding = new FakeFoundingUnitOfWork();
        var handler = Handler(FakeCaller.Unconfirmed(), founding);

        Assert.IsType<FoundCompanyOutcome.NotConfirmed>(await handler.HandleAsync(Reference(), Valid, Valid, Token));

        // The body is checked first: an invalid body without the level is still answered 422.
        Assert.Equal(
            [FoundCompanyField.OrganizationName],
            Assert.IsType<FoundCompanyOutcome.Invalid>(await handler.HandleAsync(Reference(), "", Valid, Token)).Fields);

        Assert.Empty(founding.Calls);
    }

    [Fact]
    public async Task ARequestWithoutACallerFoundsNothing()
    {
        var founding = new FakeFoundingUnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Handler(new FakeCaller(null), founding).HandleAsync(Reference(), Valid, Valid, Token));

        Assert.Empty(founding.Calls);
    }

    [Fact]
    public async Task TheFoundingIsForTheCallerUnderTheReferenceWithTheMeasuredNames()
    {
        var caller = FakeCaller.Confirmed();
        var founding = new FakeFoundingUnitOfWork();
        var reference = Reference();

        var outcome = await Handler(caller, founding).HandleAsync(reference, "  Kouassi BTP ", " Siège ", Token);

        var founded = Assert.IsType<FoundCompanyOutcome.Founded>(outcome).Company;
        var call = Assert.Single(founding.Calls);
        Assert.Equal(reference, call.Reference.Value);
        Assert.Equal(caller.Current!.Subject, call.Founder);
        Assert.Equal("Kouassi BTP", call.OrganizationName.Value);
        Assert.Equal("Siège", call.WorkspaceName.Value);
        Assert.Equal(reference, founded.Organization.Id.Value);
        Assert.Equal(OrganizationStatus.Declared, founded.Organization.Status);
    }

    [Fact]
    public async Task AReferenceInUseByAnOrganizationTheCallerOwnsIsAReplayOfThatOrganization()
    {
        var caller = FakeCaller.Confirmed();
        var reference = Reference();
        var owned = OrganizationUnder(reference, caller);
        var founding = new FakeFoundingUnitOfWork { ReferenceInUse = true, Owned = owned };

        var outcome = await Handler(caller, founding).HandleAsync(reference, "Another name", "Another site", Token);

        Assert.Same(owned, Assert.IsType<FoundCompanyOutcome.Replayed>(outcome).Organization);
        var read = Assert.Single(founding.OwnedReads);
        Assert.Equal(reference, read.Reference.Value);
        Assert.Equal(caller.Current!.Subject, read.Owner);
    }

    [Fact]
    public async Task AReferenceInUseByAnOrganizationTheCallerDoesNotOwnIsInUse()
    {
        var caller = FakeCaller.Confirmed();
        var founding = new FakeFoundingUnitOfWork { ReferenceInUse = true, Owned = null };

        var outcome = await Handler(caller, founding).HandleAsync(Reference(), Valid, Valid, Token);

        Assert.IsType<FoundCompanyOutcome.ReferenceInUse>(outcome);
        Assert.Equal(caller.Current!.Subject, Assert.Single(founding.OwnedReads).Owner);
    }

    [Fact]
    public async Task AFailureOfTheFoundingOrOfTheOwnershipReadPropagates()
    {
        var failure = new InvalidOperationException("forced by the test");
        var failingFounding = new FakeFoundingUnitOfWork { FoundingFailure = failure };
        var failingRead = new FakeFoundingUnitOfWork { ReferenceInUse = true, ReadFailure = failure };

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(FakeCaller.Confirmed(), failingFounding).HandleAsync(Reference(), Valid, Valid, Token)));
        Assert.Empty(failingFounding.OwnedReads);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(FakeCaller.Confirmed(), failingRead).HandleAsync(Reference(), Valid, Valid, Token)));
    }

    private static Organization OrganizationUnder(Guid reference, FakeCaller caller) =>
        SaasBtp.Access.Domain.Founding.CompanyFounding.Found(
            OrganizationId.TryCreate(reference, out var id) ? id : throw new InvalidOperationException("not a UUIDv7"),
            OrganizationName.TryCreate(Valid, out var name, out _) ? name : throw new InvalidOperationException(Valid),
            SaasBtp.Access.Domain.Workspaces.WorkspaceName.TryCreate(Valid, out var workspace, out _) ? workspace : throw new InvalidOperationException(Valid),
            caller.Current!.Subject,
            founderProfile: null,
            TimeProvider.System).Organization;

    private static FoundCompanyHandler Handler(FakeCaller caller, FakeFoundingUnitOfWork founding) => new(caller, founding);

    private static Guid Reference() => Guid.CreateVersion7();

    private static Task<FoundCompanyOutcome> SubmitAsync(FakeFoundingUnitOfWork founding, string field, string name) =>
        Handler(FakeCaller.Confirmed(), founding).HandleAsync(
            Reference(),
            field == FoundCompanyField.OrganizationName ? name : Valid,
            field == FoundCompanyField.WorkspaceName ? name : Valid,
            Token);

    private static async Task AssertRefusedAsync(string field, string name)
    {
        var founding = new FakeFoundingUnitOfWork();

        var outcome = await SubmitAsync(founding, field, name);

        Assert.Equal([field], Assert.IsType<FoundCompanyOutcome.Invalid>(outcome).Fields);
        Assert.Empty(founding.Calls);
    }

    private static async Task AssertFoundedAsync(string field, string name, string written)
    {
        var founding = new FakeFoundingUnitOfWork();

        Assert.IsType<FoundCompanyOutcome.Founded>(await SubmitAsync(founding, field, name));

        var call = Assert.Single(founding.Calls);
        Assert.Equal(written, field == FoundCompanyField.OrganizationName ? call.OrganizationName.Value : call.WorkspaceName.Value);
    }
}
