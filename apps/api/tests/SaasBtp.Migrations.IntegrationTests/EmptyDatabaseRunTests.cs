using SaasBtp.Testing;

namespace SaasBtp.Migrations.IntegrationTests;

public sealed class EmptyDatabaseRunTests(EmptyDatabaseRun run) : IClassFixture<EmptyDatabaseRun>
{
    [Fact]
    public void TheFirstRunSucceeds()
    {
        Assert.True(run.FirstRun.ExitCode == 0, run.FirstRun.Output);
    }

    [Fact]
    public void TheFirstRunCreatesTheJournalInTheDbupSchema()
    {
        Assert.Contains(EmptyDatabaseRun.JournalTable, run.JournalSchemaAfterFirstRun);
    }

    [Fact]
    public void TheFirstRunJournalsEveryScriptTheRunnerCarriesExceptTheAuthCoupledOnes()
    {
        var expected = EmptyDatabaseRun.EmbeddedScripts
            .Where(script => !AuthCoupledScripts.FileNames.Any(name => script.EndsWith("." + name, StringComparison.Ordinal)))
            .ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(EmptyDatabaseRun.EmbeddedScripts.Count - AuthCoupledScripts.FileNames.Count, expected.Count);
        Assert.Equal(expected, run.JournaledScriptsAfterFirstRun);
        Assert.Equal(expected.Count, run.JournalEntriesAfterFirstRun);
    }

    [Fact]
    public void TheFirstRunNamesEachScriptItExcludes()
    {
        Assert.NotEmpty(AuthCoupledScripts.FileNames);
        Assert.All(
            AuthCoupledScripts.FileNames,
            name => Assert.Contains($"Excluded, not applied: {name}", run.FirstRun.Output, StringComparison.Ordinal));
    }

    [Fact]
    public void TheFirstRunCreatesTheAccessTablesAndFunctions()
    {
        string[] tables = ["memberships", "organizations", "ownerships", "profiles", "workspace_accesses", "workspaces"];

        Assert.All(tables, table => Assert.Contains($"relation:{table}", run.AccessSchemaAfterFirstRun));
        Assert.Equal(
            ["function:current_profile_id", "function:may_act_in"],
            run.AccessSchemaAfterFirstRun.Where(name => name.StartsWith("function:", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheRunsCreateNothingInThePublicSchema()
    {
        Assert.Equal(run.PublicBefore, run.PublicAfter);
        Assert.DoesNotContain(run.PublicAfter, name => name.StartsWith("relation:", StringComparison.Ordinal));
    }

    [Fact]
    public void ASecondRunSucceedsAndAppliesNothing()
    {
        Assert.True(run.SecondRun.ExitCode == 0, run.SecondRun.Output);
        Assert.Equal(run.JournalSchemaAfterFirstRun, run.JournalSchemaAfterSecondRun);
        Assert.Equal(run.JournalEntriesAfterFirstRun, run.JournalEntriesAfterSecondRun);
        Assert.Contains("No new scripts need to be executed", run.SecondRun.Output, StringComparison.Ordinal);
    }
}
