using MicroKit.Domain.Aggregates;
using MicroKit.Result;

namespace SaasBtp.Safety.Domain.Constats;

/// <summary>
/// A safety observation made on a site, at a point in time, by an identified person: the aggregate
/// root of the Safety bounded context.
/// </summary>
/// <remarks>
/// A constat is a fact (a finding), kept faithful to the moment it happened; it is not "resolved"
/// inside Safety (safety-domain-model.md §1/§2). Treatment (assignment, deadline, status, closure)
/// lives in CorrectiveActions (ADR-ARCH-002); media lives in the Media context; neither is part of
/// this aggregate. Insert-only in this story: no status, no mutation, and no domain event is raised
/// (§8 — the <c>SafetyConstatRaised</c> seam arrives with the CorrectiveActions slice). The single
/// entry point is <see cref="Create"/>; the constructors are private.
/// </remarks>
public sealed class Constat : AggregateRoot<ConstatId>
{
    /// <summary>
    /// How far business time (<see cref="OccurredAt"/>) may lead server time
    /// (<see cref="CreatedAt"/>) before a capture is rejected.
    /// </summary>
    /// <remarks>
    /// A slightly-fast field device must not have its capture rejected at sync time, so the
    /// invariant is <c>occurredAt &lt;= createdAt + tolerance</c>, NOT <c>occurredAt &lt;= now</c>
    /// (safety-domain-model.md §4/§7). Deterministic and offline-replay-safe.
    /// </remarks>
    public static readonly TimeSpan ClockDriftTolerance = TimeSpan.FromMinutes(5);

    // EF Core materialization constructor. EF instantiates via this parameterless ctor and then
    // populates the get-only properties (including the owned Observer/Location/Observation and the
    // converted ids/tokens) through their backing fields. The domain never calls it — Create is the
    // only real entry point, and the invariant gate. base(default) is safe: ConstatId is a struct,
    // so Entity<TId>'s null check never trips, and EF overwrites Id from the key column.
#pragma warning disable CS8618 // Non-null members are set by EF via backing fields after construction.
    private Constat()
        : base(default)
    {
    }
#pragma warning restore CS8618

    private Constat(
        ConstatId id,
        TenantId tenantId,
        SiteId siteId,
        ConstatType type,
        Severity severity,
        ObserverSnapshot observer,
        DateTime occurredAt,
        Location location,
        Observation observation,
        DateTime createdAt)
        : base(id)
    {
        TenantId = tenantId;
        SiteId = siteId;
        Type = type;
        Severity = severity;
        Observer = observer;
        OccurredAt = occurredAt;
        Location = location;
        Observation = observation;
        CreatedAt = createdAt;
    }

    /// <summary>The tenant this constat belongs to (isolation boundary).</summary>
    public TenantId TenantId { get; }

    /// <summary>The site (chantier) the constat was observed on.</summary>
    public SiteId SiteId { get; }

    /// <summary>The kind of finding.</summary>
    public ConstatType Type { get; }

    /// <summary>How serious the finding is.</summary>
    public Severity Severity { get; }

    /// <summary>The observer, frozen at creation (proof value).</summary>
    public ObserverSnapshot Observer { get; }

    /// <summary>
    /// Business time: when the safety event happened (client-provided, immutable). This is what
    /// dashboards, filters and QHSE KPIs read — never <see cref="CreatedAt"/> (§7).
    /// </summary>
    public DateTime OccurredAt { get; }

    /// <summary>Where on the site the finding was observed.</summary>
    public Location Location { get; }

    /// <summary>The narrative of the finding.</summary>
    public Observation Observation { get; }

    /// <summary>
    /// Technical audit time: when the constat was persisted (server-set, never client-provided).
    /// Supplied to <see cref="Create"/> by the caller (the server clock), so the aggregate itself
    /// generates no time (§7, ADR-ARCH-005).
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Creates a valid <see cref="Constat"/>, or a typed validation failure. The only way to
    /// construct the aggregate; it is the invariant gate (§4, §5).
    /// </summary>
    /// <remarks>
    /// Gate contract: every expected-invalid input MUST be observable through this method's
    /// <see cref="Result{T}"/>. No invalid input may be detected for the first time by persistence,
    /// serialization, or a CLR exception — the factory rejects it here as a typed failure, and it
    /// never coerces an invalid value into a valid one (see the non-UTC and null-description guards).
    /// <para>
    /// Call-site discipline: <paramref name="occurredAt"/> and <paramref name="createdAt"/> are both
    /// bare <see cref="DateTime"/>s, so transposing them compiles and silently corrupts a proof-value
    /// timestamp (§7). Always pass them as NAMED arguments (<c>occurredAt:</c>, <c>createdAt:</c>).
    /// </para>
    /// </remarks>
    /// <param name="id">Client-generated UUIDv7 id (also the idempotency key). Must be non-empty.</param>
    /// <param name="tenantId">The owning tenant. Must be non-empty.</param>
    /// <param name="siteId">The site the constat belongs to. Must be non-empty.</param>
    /// <param name="type">The kind of finding. Required.</param>
    /// <param name="severity">The severity. Required.</param>
    /// <param name="observer">The observer snapshot (derived from the identity). Required.</param>
    /// <param name="occurredAt">
    /// Business time of the event. Required, and no further ahead of <paramref name="createdAt"/>
    /// than <see cref="ClockDriftTolerance"/>.
    /// </param>
    /// <param name="location">The location (present; description may be empty). Required.</param>
    /// <param name="observation">The narrative; its short description must be non-empty.</param>
    /// <param name="createdAt">Server-provided audit time. Immutable once set.</param>
    /// <returns>
    /// <see cref="Result{T}"/> of <see cref="Constat"/>: success, or a <see cref="ConstatErrors"/>
    /// validation failure. Never throws for expected-invalid input (offline-replay-safe, §4).
    /// </returns>
    public static Result<Constat> Create(
        ConstatId id,
        TenantId tenantId,
        SiteId siteId,
        ConstatType type,
        Severity severity,
        ObserverSnapshot observer,
        DateTime occurredAt,
        Location location,
        Observation observation,
        DateTime createdAt)
    {
        // Required references — all deterministic and offline-verifiable (§4). Rules needing
        // server/cross-context state (the site is open, the responsable exists) are NOT constat
        // invariants; they belong to other workflows (§4, ADR-ARCH-005).
        if (id.Value == Guid.Empty)
            return Result<Constat>.Failure(ConstatErrors.IdRequired);
        if (tenantId.Value == Guid.Empty)
            return Result<Constat>.Failure(ConstatErrors.TenantRequired);
        if (siteId.Value == Guid.Empty)
            return Result<Constat>.Failure(ConstatErrors.SiteRequired);
        if (type is null)
            return Result<Constat>.Failure(ConstatErrors.TypeRequired);
        if (severity is null)
            return Result<Constat>.Failure(ConstatErrors.SeverityRequired);
        if (observer is null)
            return Result<Constat>.Failure(ConstatErrors.ObserverRequired);

        // The snapshot is the constat's proof of authorship (§3): even though the observer is
        // derived from the trusted identity (§5), the aggregate never surrenders an invariant it can
        // check itself (§4). An empty user id or blank name would leave "who reported this?"
        // unanswerable, so reject both. Function stays optional.
        if (observer.UserId == Guid.Empty)
            return Result<Constat>.Failure(ConstatErrors.ObserverUserIdRequired);
        if (string.IsNullOrWhiteSpace(observer.FullName))
            return Result<Constat>.Failure(ConstatErrors.ObserverFullNameRequired);

        if (location is null)
            return Result<Constat>.Failure(ConstatErrors.LocationRequired);
        // Description may be empty (the model lists no non-empty rule for location) but never null:
        // location_description is NOT NULL, so a null would otherwise escape the gate and surface as
        // a DbUpdateException at SaveChanges. The domain rejects it as a typed Result — it does NOT
        // coerce null to empty (the same "guess and silently corrupt" we refused for non-UTC time).
        if (location.Description is null)
            return Result<Constat>.Failure(ConstatErrors.LocationDescriptionRequired);

        // A constat with no observation does not exist (§4). Container-null first, then field
        // validity — mirroring the Observer gate (OBSERVER_REQUIRED, then the field checks) so a
        // caller never learns the false rule "short description required" for a missing container.
        if (observation is null)
            return Result<Constat>.Failure(ConstatErrors.ObservationRequired);
        if (string.IsNullOrWhiteSpace(observation.ShortDescription))
            return Result<Constat>.Failure(ConstatErrors.ShortDescriptionRequired);

        if (occurredAt == default)
            return Result<Constat>.Failure(ConstatErrors.OccurredAtRequired);

        // Both timestamps land in timestamptz columns, and Npgsql throws when a non-UTC DateTime is
        // written there (a Local/Unspecified kind maps to timestamp WITHOUT time zone). The aggregate
        // owns the "stored UTC" guarantee (§7, ADR-ARCH-005), so it rejects a non-UTC value here as a
        // typed Result failure rather than letting it surface as an infrastructure exception at
        // SaveChanges. It REJECTS, never coerces: converting an Unspecified kind would mean guessing
        // the field device's timezone and silently corrupting a proof-value timestamp.
        if (occurredAt.Kind != DateTimeKind.Utc)
            return Result<Constat>.Failure(ConstatErrors.OccurredAtNotUtc);
        if (createdAt.Kind != DateTimeKind.Utc)
            return Result<Constat>.Failure(ConstatErrors.CreatedAtNotUtc);

        // occurredAt may slightly lead server time, but not beyond the tolerance (§4/§7).
        if (occurredAt > createdAt + ClockDriftTolerance)
            return Result<Constat>.Failure(ConstatErrors.OccurredAtAfterCreatedAt);

        var constat = new Constat(
            id, tenantId, siteId, type, severity, observer, occurredAt, location, observation, createdAt);

        // No domain event raised in this story (§8): the aggregate persists, it does not raise.
        return Result<Constat>.Success(constat);
    }
}
