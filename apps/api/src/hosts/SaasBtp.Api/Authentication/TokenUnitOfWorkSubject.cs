using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

namespace SaasBtp.Api.Authentication;

/// <summary>
/// The host's implementation of the copied provider's subject port (ADR-ARCH-035 §8,
/// ADR-ARCH-033 §4): the <c>sub</c> of the principal the host authenticated from the validated
/// token (ADR-ARCH-038 decision 3), set in <c>app.user_id</c> by every transaction; none without
/// an authenticated principal, so that no transaction starts.
/// </summary>
/// <remarks>A singleton that reads the current request at each call.</remarks>
internal sealed class TokenUnitOfWorkSubject(IHttpContextAccessor requests) : IUnitOfWorkSubject
{
    public Guid? Current => AccessTokenClaims.SubjectOf(requests.HttpContext?.User);
}
