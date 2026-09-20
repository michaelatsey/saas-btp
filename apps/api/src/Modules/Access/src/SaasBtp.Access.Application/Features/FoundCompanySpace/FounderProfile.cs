using MicroKit.Domain.ValueObjects.Common;

namespace SaasBtp.Access.Application.Features.FoundCompanySpace;

/// <summary>
/// The founder's identity projection — the data written to <c>access.profiles</c>.
/// </summary>
/// <remarks>
/// Deliberately NOT a Domain type: the projection is infrastructure the business facts stand on,
/// never a business fact itself (Design Package §0). It is carried as application data and written
/// inside the founding transaction, first among the writes.
/// </remarks>
/// <param name="UserId">The authenticated identity (equals <c>auth.users.id</c>).</param>
/// <param name="Email">
/// The founder's e-mail. Typed as MicroKit's <see cref="MicroKit.Domain.ValueObjects.Common.Email"/>
/// value object, which normalizes on construction — so ADR-ARCH-011's "normalized at every write and
/// every comparison" rule cannot be forgotten by a future caller.
/// </param>
/// <param name="FullName">The founder's full name.</param>
/// <param name="CreatedAtUtc">The founding instant — domain-supplied, never a database default.</param>
public sealed record FounderProfile(
    Guid UserId,
    Email Email,
    string FullName,
    DateTime CreatedAtUtc);
