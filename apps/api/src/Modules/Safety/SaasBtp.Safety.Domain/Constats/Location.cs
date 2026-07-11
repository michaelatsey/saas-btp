using MicroKit.Domain.ValueObjects;

namespace SaasBtp.Safety.Domain.Constats;

/// <summary>
/// Where on the site the finding was observed — a free-text description in this story.
/// </summary>
/// <remarks>
/// <see cref="Description"/> is required to be present (non-null) but an empty string is tolerated
/// (safety-domain-model.md brief: "NOT NULL, empty tolerated") — hence no non-empty invariant.
/// Coordinates (lat/long) are a deliberate future extension (§9), not modeled now.
/// </remarks>
/// <param name="Description">The free-text location description (may be empty, never null).</param>
public sealed record Location(string Description) : IValueObject;
