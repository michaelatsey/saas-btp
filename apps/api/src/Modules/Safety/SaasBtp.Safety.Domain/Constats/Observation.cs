using MicroKit.Domain.ValueObjects;

namespace SaasBtp.Safety.Domain.Constats;

/// <summary>
/// The narrative of the finding: a required short summary plus optional detail and extra notes.
/// </summary>
/// <remarks>
/// Groups the free-text fields of the constat (safety-domain-model.md §3). Only
/// <see cref="ShortDescription"/> is invariant-required (non-empty) — a constat with no observation
/// does not exist (§4). The other two are optional. Validation is enforced by
/// <see cref="Constat.Create"/> (returned as a Result failure), not in this Value Object.
/// </remarks>
/// <param name="ShortDescription">Required short summary of the finding.</param>
/// <param name="DetailedDescription">Optional full circumstances.</param>
/// <param name="Observations">Optional extra notes.</param>
public sealed record Observation(
    string ShortDescription,
    string? DetailedDescription,
    string? Observations) : IValueObject;
