using MicroKit.Result;

namespace SaasBtp.Safety.Domain.Constats;

/// <summary>
/// Typed validation errors for the Constat aggregate: the invariants enforced by
/// <see cref="Constat.Create"/> and the closed-set <c>FromToken</c> parsers.
/// </summary>
/// <remarks>
/// All are <see cref="ErrorCategory.Validation"/>, so a driving adapter (branch 2) maps them to an
/// HTTP 4xx without the domain knowing about HTTP. They are returned as <see cref="Result{T}"/>
/// failures — never thrown — so an offline replay batch validates deterministically
/// (safety-domain-model.md §4). Codes follow the <c>DOMAIN.ENTITY.RULE</c> convention.
/// </remarks>
public static class ConstatErrors
{
    /// <summary>The constat id (client-generated UUIDv7) is required and must be non-empty.</summary>
    public static readonly IError IdRequired = new IdRequiredError();

    /// <summary>The tenant id is required.</summary>
    public static readonly IError TenantRequired = new TenantRequiredError();

    /// <summary>The site id is required (a constat is intrinsically attached to a chantier).</summary>
    public static readonly IError SiteRequired = new SiteRequiredError();

    /// <summary>The constat type is required.</summary>
    public static readonly IError TypeRequired = new TypeRequiredError();

    /// <summary>The severity is required.</summary>
    public static readonly IError SeverityRequired = new SeverityRequiredError();

    /// <summary>The observer snapshot is required (derived from the authenticated identity).</summary>
    public static readonly IError ObserverRequired = new ObserverRequiredError();

    /// <summary>The observer's user id is required (a constat must name who reported it — §3).</summary>
    public static readonly IError ObserverUserIdRequired = new ObserverUserIdRequiredError();

    /// <summary>The observer's full name is required (proof value: "who reported this risk?" — §3).</summary>
    public static readonly IError ObserverFullNameRequired = new ObserverFullNameRequiredError();

    /// <summary>The location is required (present, though its description may be empty).</summary>
    public static readonly IError LocationRequired = new LocationRequiredError();

    /// <summary>
    /// The location description is required to be present (non-null); an empty string is tolerated.
    /// </summary>
    public static readonly IError LocationDescriptionRequired = new LocationDescriptionRequiredError();

    /// <summary>The observation is required (the narrative container must be present — §4).</summary>
    public static readonly IError ObservationRequired = new ObservationRequiredError();

    /// <summary>The observation's short description is required and must be non-empty.</summary>
    public static readonly IError ShortDescriptionRequired = new ShortDescriptionRequiredError();

    /// <summary>The occurrence time is required.</summary>
    public static readonly IError OccurredAtRequired = new OccurredAtRequiredError();

    /// <summary>
    /// The occurrence time is further ahead of server time than the clock-drift tolerance allows.
    /// </summary>
    public static readonly IError OccurredAtAfterCreatedAt = new OccurredAtAfterCreatedAtError();

    /// <summary>The occurrence time must be UTC (stored as timestamptz; a non-UTC value is rejected).</summary>
    public static readonly IError OccurredAtNotUtc = new OccurredAtNotUtcError();

    /// <summary>The audit time must be UTC (stored as timestamptz; a non-UTC value is rejected).</summary>
    public static readonly IError CreatedAtNotUtc = new CreatedAtNotUtcError();

    /// <summary>An unknown/null <see cref="ConstatType"/> token was supplied to <c>FromToken</c>.</summary>
    /// <param name="token">The offending token (may be null).</param>
    /// <returns>A validation error carrying the offending token.</returns>
    public static IError UnknownConstatType(string? token) => new UnknownConstatTypeError(token);

    /// <summary>An unknown/null <see cref="Severity"/> token was supplied to <c>FromToken</c>.</summary>
    /// <param name="token">The offending token (may be null).</param>
    /// <returns>A validation error carrying the offending token.</returns>
    public static IError UnknownSeverity(string? token) => new UnknownSeverityError(token);

    private sealed record IdRequiredError()
        : Error(ErrorCode.From("SAFETY.CONSTAT.ID_REQUIRED"), "A constat id is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record TenantRequiredError()
        : Error(ErrorCode.From("SAFETY.CONSTAT.TENANT_REQUIRED"), "A tenant id is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record SiteRequiredError()
        : Error(ErrorCode.From("SAFETY.CONSTAT.SITE_REQUIRED"), "A site id is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record TypeRequiredError()
        : Error(ErrorCode.From("SAFETY.CONSTAT.TYPE_REQUIRED"), "A constat type is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record SeverityRequiredError()
        : Error(ErrorCode.From("SAFETY.CONSTAT.SEVERITY_REQUIRED"), "A severity is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record ObserverRequiredError()
        : Error(ErrorCode.From("SAFETY.CONSTAT.OBSERVER_REQUIRED"), "An observer is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record ObserverUserIdRequiredError()
        : Error(ErrorCode.From("SAFETY.CONSTAT.OBSERVER_USER_ID_REQUIRED"), "An observer user id is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record ObserverFullNameRequiredError()
        : Error(
            ErrorCode.From("SAFETY.CONSTAT.OBSERVER_FULL_NAME_REQUIRED"),
            "A non-empty observer full name is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record LocationRequiredError()
        : Error(ErrorCode.From("SAFETY.CONSTAT.LOCATION_REQUIRED"), "A location is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record LocationDescriptionRequiredError()
        : Error(
            ErrorCode.From("SAFETY.CONSTAT.LOCATION_DESCRIPTION_REQUIRED"),
            "A location description is required (an empty string is allowed, null is not).")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record ObservationRequiredError()
        : Error(ErrorCode.From("SAFETY.CONSTAT.OBSERVATION_REQUIRED"), "An observation is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record ShortDescriptionRequiredError()
        : Error(
            ErrorCode.From("SAFETY.CONSTAT.SHORT_DESCRIPTION_REQUIRED"),
            "A non-empty short description is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record OccurredAtRequiredError()
        : Error(ErrorCode.From("SAFETY.CONSTAT.OCCURRED_AT_REQUIRED"), "An occurrence time is required.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record OccurredAtAfterCreatedAtError()
        : Error(
            ErrorCode.From("SAFETY.CONSTAT.OCCURRED_AT_AFTER_CREATED_AT"),
            "The occurrence time is too far ahead of server time (beyond the clock-drift tolerance).")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record OccurredAtNotUtcError()
        : Error(
            ErrorCode.From("SAFETY.CONSTAT.OCCURRED_AT_NOT_UTC"),
            "The occurrence time must be expressed in UTC (DateTimeKind.Utc).")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record CreatedAtNotUtcError()
        : Error(
            ErrorCode.From("SAFETY.CONSTAT.CREATED_AT_NOT_UTC"),
            "The audit time must be expressed in UTC (DateTimeKind.Utc).")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record UnknownConstatTypeError(string? Token)
        : Error(
            ErrorCode.From("SAFETY.CONSTAT.UNKNOWN_TYPE"),
            $"Unknown constat type token '{Token}'.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    private sealed record UnknownSeverityError(string? Token)
        : Error(
            ErrorCode.From("SAFETY.CONSTAT.UNKNOWN_SEVERITY"),
            $"Unknown severity token '{Token}'.")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }
}
