namespace SaasBtp.Safety.UnitTests;

/// <summary>
/// The Constat factory is the invariant gate (safety-domain-model.md §4/§5): valid input succeeds,
/// every invariant rejects as a typed <see cref="Result{T}"/> failure (never an exception), and the
/// aggregate raises no domain event this story (§8).
/// </summary>
public sealed class ConstatCreateTests
{
    private static readonly ObserverSnapshot Observer = new(Guid.CreateVersion7(), "Jeanne Martin", "Chef de chantier");
    private static readonly Location ValidLocation = new("Niveau 2, aile est");
    private static readonly Observation ValidObservation = new("Garde-corps manquant", "Détail", "Notes");

    // Ids are constructed explicitly with Guid.CreateVersion7() in test code — the domain never
    // generates ids (safety-domain-model.md §6).
    private static ConstatId NewConstatId() => new(Guid.CreateVersion7());

    private static Result<Constat> CreateValid(
        DateTime? occurredAt = null,
        DateTime? createdAt = null,
        ConstatId? id = null,
        TenantId? tenantId = null,
        SiteId? siteId = null,
        ConstatType? type = null,
        Severity? severity = null,
        ObserverSnapshot? observer = null,
        Location? location = null,
        Observation? observation = null)
    {
        var created = createdAt ?? new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc);
        return Constat.Create(
            id ?? NewConstatId(),
            tenantId ?? new TenantId(Guid.CreateVersion7()),
            siteId ?? new SiteId(Guid.CreateVersion7()),
            type ?? ConstatType.Incident,
            severity ?? Severity.Major,
            observer ?? Observer,
            occurredAt ?? created,
            location ?? ValidLocation,
            observation ?? ValidObservation,
            created);
    }

    private static void ShouldFailWith(Result<Constat> result, string expectedCode)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.Value.ShouldBe(expectedCode);
    }

    [Fact]
    public void Create_WithValidInput_Succeeds_AndRoundTripsFields()
    {
        var id = NewConstatId();
        var tenantId = new TenantId(Guid.CreateVersion7());
        var siteId = new SiteId(Guid.CreateVersion7());
        var occurredAt = new DateTime(2026, 7, 9, 8, 30, 0, DateTimeKind.Utc);
        var createdAt = new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc);

        var result = CreateValid(occurredAt, createdAt, id, tenantId, siteId,
            ConstatType.DangerousSituation, Severity.Critical);

        result.IsSuccess.ShouldBeTrue();
        var constat = result.Value;
        constat.Id.ShouldBe(id);
        constat.TenantId.ShouldBe(tenantId);
        constat.SiteId.ShouldBe(siteId);
        constat.Type.ShouldBe(ConstatType.DangerousSituation);
        constat.Severity.ShouldBe(Severity.Critical);
        constat.Observer.ShouldBe(Observer);
        constat.OccurredAt.ShouldBe(occurredAt);
        constat.Location.ShouldBe(ValidLocation);
        constat.Observation.ShouldBe(ValidObservation);
        constat.CreatedAt.ShouldBe(createdAt);
    }

    [Fact]
    public void Create_RaisesNoDomainEvent()
    {
        var constat = CreateValid().Value;

        // §8: the aggregate persists, it does not raise. The SafetyConstatRaised seam is a later slice.
        constat.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithEmptyId_Fails() =>
        ShouldFailWith(CreateValid(id: new ConstatId(Guid.Empty)), "SAFETY.CONSTAT.ID_REQUIRED");

    [Fact]
    public void Create_WithEmptyTenantId_Fails() =>
        ShouldFailWith(CreateValid(tenantId: new TenantId(Guid.Empty)), "SAFETY.CONSTAT.TENANT_REQUIRED");

    [Fact]
    public void Create_WithEmptySiteId_Fails() =>
        ShouldFailWith(CreateValid(siteId: new SiteId(Guid.Empty)), "SAFETY.CONSTAT.SITE_REQUIRED");

    [Fact]
    public void Create_WithNullType_Fails() =>
        ShouldFailWith(
            Constat.Create(NewConstatId(), new TenantId(Guid.CreateVersion7()), new SiteId(Guid.CreateVersion7()),
                type: null!, Severity.Major, Observer, DateTime.UtcNow, ValidLocation, ValidObservation, DateTime.UtcNow),
            "SAFETY.CONSTAT.TYPE_REQUIRED");

    [Fact]
    public void Create_WithNullSeverity_Fails() =>
        ShouldFailWith(
            Constat.Create(NewConstatId(), new TenantId(Guid.CreateVersion7()), new SiteId(Guid.CreateVersion7()),
                ConstatType.Incident, severity: null!, Observer, DateTime.UtcNow, ValidLocation, ValidObservation, DateTime.UtcNow),
            "SAFETY.CONSTAT.SEVERITY_REQUIRED");

    [Fact]
    public void Create_WithNullObserver_Fails() =>
        ShouldFailWith(
            Constat.Create(NewConstatId(), new TenantId(Guid.CreateVersion7()), new SiteId(Guid.CreateVersion7()),
                ConstatType.Incident, Severity.Major, observer: null!, DateTime.UtcNow, ValidLocation, ValidObservation, DateTime.UtcNow),
            "SAFETY.CONSTAT.OBSERVER_REQUIRED");

    [Fact]
    public void Create_WithEmptyObserverUserId_Fails() =>
        // The snapshot is proof of authorship (§3): the zero uuid leaves "who reported this?" unanswerable.
        ShouldFailWith(
            CreateValid(observer: new ObserverSnapshot(Guid.Empty, "Jeanne Martin", "Chef de chantier")),
            "SAFETY.CONSTAT.OBSERVER_USER_ID_REQUIRED");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankObserverFullName_Fails(string fullName) =>
        // Symmetric with ShortDescription: a blank name defeats the snapshot's proof value (§3).
        ShouldFailWith(
            CreateValid(observer: new ObserverSnapshot(Guid.CreateVersion7(), fullName, null)),
            "SAFETY.CONSTAT.OBSERVER_FULL_NAME_REQUIRED");

    [Fact]
    public void Create_WithNullLocation_Fails() =>
        ShouldFailWith(
            Constat.Create(NewConstatId(), new TenantId(Guid.CreateVersion7()), new SiteId(Guid.CreateVersion7()),
                ConstatType.Incident, Severity.Major, Observer, DateTime.UtcNow, location: null!, ValidObservation, DateTime.UtcNow),
            "SAFETY.CONSTAT.LOCATION_REQUIRED");

    [Fact]
    public void Create_WithEmptyLocationDescription_Succeeds()
    {
        // Location is required to be present, but an empty description is tolerated (§4 / brief).
        var result = CreateValid(location: new Location(string.Empty));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Location.Description.ShouldBe(string.Empty);
    }

    [Fact]
    public void Create_WithNullLocationDescription_Fails() =>
        // Location is present but its description is null: empty is tolerated, null is not. The gate
        // rejects it as a typed Result instead of letting it throw at SaveChanges (location_description
        // is NOT NULL). The domain rejects; it does not coerce null to empty.
        ShouldFailWith(
            CreateValid(location: new Location(null!)),
            "SAFETY.CONSTAT.LOCATION_DESCRIPTION_REQUIRED");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankShortDescription_Fails(string shortDescription) =>
        ShouldFailWith(
            CreateValid(observation: new Observation(shortDescription, null, null)),
            "SAFETY.CONSTAT.SHORT_DESCRIPTION_REQUIRED");

    [Fact]
    public void Create_WithNullObservation_Fails() =>
        // Container-null is its own code (mirrors OBSERVER_REQUIRED vs the field checks): a missing
        // Observation must not masquerade as a blank short description.
        ShouldFailWith(
            Constat.Create(NewConstatId(), new TenantId(Guid.CreateVersion7()), new SiteId(Guid.CreateVersion7()),
                ConstatType.Incident, Severity.Major, Observer, DateTime.UtcNow, ValidLocation, observation: null!, DateTime.UtcNow),
            "SAFETY.CONSTAT.OBSERVATION_REQUIRED");

    [Fact]
    public void Create_WithUnsetOccurredAt_Fails() =>
        ShouldFailWith(
            CreateValid(occurredAt: default(DateTime), createdAt: new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc)),
            "SAFETY.CONSTAT.OCCURRED_AT_REQUIRED");

    [Fact]
    public void Create_OccurredAtExactlyAtDriftTolerance_Succeeds()
    {
        var createdAt = new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc);
        var occurredAt = createdAt + Constat.ClockDriftTolerance;

        // Boundary is inclusive: occurredAt <= createdAt + tolerance (§4).
        CreateValid(occurredAt, createdAt).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_OccurredAtBeyondDriftTolerance_Fails()
    {
        var createdAt = new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc);
        var occurredAt = createdAt + Constat.ClockDriftTolerance + TimeSpan.FromSeconds(1);

        ShouldFailWith(CreateValid(occurredAt, createdAt), "SAFETY.CONSTAT.OCCURRED_AT_AFTER_CREATED_AT");
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)] // the branch-2 DTO case: no Z/offset -> Unspecified
    [InlineData(DateTimeKind.Local)]
    public void Create_WithNonUtcOccurredAt_Fails(DateTimeKind kind)
    {
        // A non-UTC value would throw at SaveChanges (timestamptz); the aggregate rejects it as a
        // typed Result instead. Within tolerance and not default, so the earlier gates pass first.
        var createdAt = new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc);
        var occurredAt = new DateTime(2026, 7, 9, 8, 30, 0, kind);

        ShouldFailWith(CreateValid(occurredAt, createdAt), "SAFETY.CONSTAT.OCCURRED_AT_NOT_UTC");
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Create_WithNonUtcCreatedAt_Fails(DateTimeKind kind)
    {
        // occurredAt is UTC so it clears its own gate; createdAt then fails the UTC check.
        var createdAt = new DateTime(2026, 7, 9, 9, 0, 0, kind);
        var occurredAt = new DateTime(2026, 7, 9, 8, 30, 0, DateTimeKind.Utc);

        ShouldFailWith(CreateValid(occurredAt, createdAt), "SAFETY.CONSTAT.CREATED_AT_NOT_UTC");
    }
}
