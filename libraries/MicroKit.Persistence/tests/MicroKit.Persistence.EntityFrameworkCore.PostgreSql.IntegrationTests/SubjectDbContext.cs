using Microsoft.EntityFrameworkCore;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql.IntegrationTests;

/// <summary>A row the tests write. The key is client-assigned, so a refused write leaves no trace.</summary>
public sealed class SubjectRow
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

/// <summary>The context the tests register through the provider. It takes only its options, so it can be pooled.</summary>
public class SubjectDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<SubjectRow> Rows => Set<SubjectRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<SubjectRow>(b =>
        {
            b.ToTable("subject_rows");
            b.Property(r => r.Id).ValueGeneratedNever();
        });
}

/// <summary>
/// The same context, counting the transactions EF Core begins: it adds the probe's counter
/// itself, since <c>UsePostgreSQL</c> exposes only the Npgsql options. Never pooled: a pooled
/// context cannot change its options in <c>OnConfiguring</c>.
/// </summary>
public sealed class CountedSubjectDbContext(DbContextOptions<CountedSubjectDbContext> options) : SubjectDbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(Probe.TransactionCounter.Instance);
}
