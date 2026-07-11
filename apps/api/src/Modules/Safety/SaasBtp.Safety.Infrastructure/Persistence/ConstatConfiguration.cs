using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaasBtp.Safety.Domain.Constats;

namespace SaasBtp.Safety.Infrastructure.Persistence;

/// <summary>
/// Maps the <see cref="Constat"/> aggregate onto <c>safety.constats</c> (the DbUp-owned table,
/// 0001_safety_create_constats.sql). Must stay in lock-step with that DDL — the anti-drift
/// integration test is the guard (ADR-ARCH-006).
/// </summary>
internal sealed class ConstatConfiguration : IEntityTypeConfiguration<Constat>
{
    public void Configure(EntityTypeBuilder<Constat> builder)
    {
        builder.ToTable("constats");

        builder.HasKey(constat => constat.Id).HasName("pk_constats");

        // Strongly-typed ids <-> uuid via explicit converters (Value <-> Guid; Npgsql maps Guid to
        // uuid natively, preserving UUIDv7 order — asserted by the integration test).
        builder.Property(constat => constat.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ConstatId(value));

        builder.Property(constat => constat.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value));

        builder.Property(constat => constat.SiteId)
            .HasColumnName("site_id")
            .HasConversion(id => id.Value, value => new SiteId(value));

        // Closed sets persisted as their snake_case token via an EXPLICIT converter — never an
        // implicit Enum/ToString — so a C# rename can't silently drift from the stored token
        // (sql.md §Closed sets). FromToken(...).Value is total for the tokens the CHECK permits.
        builder.Property(constat => constat.Type)
            .HasColumnName("type")
            .HasMaxLength(32)
            .HasConversion(type => type.Token, token => ConstatType.FromToken(token).Value);

        builder.Property(constat => constat.Severity)
            .HasColumnName("severity")
            .HasMaxLength(10)
            .HasConversion(severity => severity.Token, token => Severity.FromToken(token).Value);

        // Business + audit time: timestamptz, stored UTC (Npgsql maps a Kind=Utc DateTime to
        // 'timestamp with time zone'; safety-domain-model.md §7). NO DB default on created_at — the
        // .NET domain is the write authority (ADR-ARCH-005), it supplies the value.
        builder.Property(constat => constat.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(constat => constat.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");

        // Owned Value Objects flattened into the constats row (table splitting) — no child tables
        // (safety-domain-model.md §3 mapping). Each owned reference is required: the factory
        // guarantees it is always present.
        builder.OwnsOne(constat => constat.Observer, observer =>
        {
            observer.Property(snapshot => snapshot.UserId).HasColumnName("observer_user_id");
            observer.Property(snapshot => snapshot.FullName)
                .HasColumnName("observer_full_name").HasMaxLength(200);
            observer.Property(snapshot => snapshot.Function)
                .HasColumnName("observer_function").HasMaxLength(150);
        });
        builder.Navigation(constat => constat.Observer).IsRequired();

        builder.OwnsOne(constat => constat.Location, location =>
        {
            // Required (non-null) but empty tolerated — no non-empty constraint (§4 / brief).
            location.Property(value => value.Description)
                .HasColumnName("location_description").HasMaxLength(300);
        });
        builder.Navigation(constat => constat.Location).IsRequired();

        builder.OwnsOne(constat => constat.Observation, observation =>
        {
            observation.Property(narrative => narrative.ShortDescription)
                .HasColumnName("short_description").HasMaxLength(300);
            // Unbounded narrative -> text (sql.md §Types): no HasMaxLength.
            observation.Property(narrative => narrative.DetailedDescription)
                .HasColumnName("detailed_description");
            observation.Property(narrative => narrative.Observations)
                .HasColumnName("observations");
        });
        builder.Navigation(constat => constat.Observation).IsRequired();
    }
}
