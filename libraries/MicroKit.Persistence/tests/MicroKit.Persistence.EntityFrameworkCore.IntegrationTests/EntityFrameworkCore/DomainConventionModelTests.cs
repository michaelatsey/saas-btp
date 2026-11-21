using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// What the conventions promise in their documentation, read off the built model: which
/// properties they convert, which they leave alone, and that explicit configuration wins.
/// </summary>
public sealed class DomainConventionModelTests
{
    [Fact]
    public void TheAuditConvention_ConvertsDiscoveredAuditInstants_AndNothingElse()
    {
        using var context = new DiscoveredMemoContext();
        var memo = context.Model.FindEntityType(typeof(Memo))!;

        Assert.Equal("UtcInstantConverter", ConverterName(memo, nameof(Memo.CreatedAt)));
        Assert.Equal("UtcInstantConverter", ConverterName(memo, nameof(Memo.UpdatedAt)));
        Assert.Null(ConverterName(memo, nameof(Memo.DueAt)));
    }

    [Fact]
    public void AnExplicitConversion_TakesPrecedenceOverTheAuditConvention()
    {
        using var context = new ExplicitMemoContext();
        var memo = context.Model.FindEntityType(typeof(Memo))!;

        Assert.Equal(nameof(DateTimeOffsetToBinaryConverter), ConverterName(memo, nameof(Memo.CreatedAt)));
        Assert.Equal("UtcInstantConverter", ConverterName(memo, nameof(Memo.UpdatedAt)));
    }

    [Fact]
    public void GuidIds_AreConverted_AsKeysAndAsOptionalProperties()
    {
        using var context = new DiscoveredMemoContext();
        var memo = context.Model.FindEntityType(typeof(Memo))!;

        Assert.Equal("GuidIdConverter", ConverterName(memo, nameof(Memo.Id)));
        Assert.Equal("GuidIdConverter", ConverterName(memo, nameof(Memo.ReplyTo)));
        Assert.True(memo.FindProperty(nameof(Memo.ReplyTo))!.IsNullable);
    }

    private static string? ConverterName(IEntityType entityType, string property) =>
        entityType.FindProperty(property)!.GetValueConverter()?.GetType().Name;

    // One context type per variant: EF Core caches the built model per context type.
    private sealed class DiscoveredMemoContext() : MemoContextBase(explicitCreatedAt: false);

    private sealed class ExplicitMemoContext() : MemoContextBase(explicitCreatedAt: true);

    private abstract class MemoContextBase(bool explicitCreatedAt) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlite("DataSource=:memory:");

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.AddDomainConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // No property is configured explicitly unless the test asks for it.
            var memo = modelBuilder.Entity<Memo>();
            memo.Ignore(m => m.DomainEvents);
            if (explicitCreatedAt)
                memo.Property(m => m.CreatedAt).HasConversion(new DateTimeOffsetToBinaryConverter());
        }
    }
}

public sealed class Memo : AuditableAggregateRoot<GuidId>
{
    private Memo(GuidId id) : base(id, DateTimeOffset.UnixEpoch, Actor.Migration) { }

    public DateTimeOffset DueAt { get; private set; }

    public GuidId? ReplyTo { get; private set; }
}
