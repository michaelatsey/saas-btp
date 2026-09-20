using MicroKit.Domain.ValueObjects;

namespace SaasBtp.Safety.Domain.Constats;

/// <summary>
/// The observer as captured at the moment the constat was recorded — a frozen snapshot, not a live
/// reference.
/// </summary>
/// <remarks>
/// A constat is a proof: it must still answer "who reported this risk?" faithfully even after the
/// person changes role or leaves (safety-domain-model.md §3). <see cref="UserId"/> is kept for
/// future relations/rights; <see cref="FullName"/> and <see cref="Function"/> are denormalized and
/// immutable. Never re-derived from the current identity on read.
/// </remarks>
/// <param name="UserId">The observer's user id (reference for future relations/rights).</param>
/// <param name="FullName">The observer's full name, frozen at creation.</param>
/// <param name="Function">The observer's function/role at creation, if known (optional).</param>
public sealed record ObserverSnapshot(Guid UserId, string FullName, string? Function) : IValueObject;
