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
