using Microsoft.EntityFrameworkCore;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Adapters.Persistence.Profiles;

/// <summary>
/// The profiles, on the Access context. An addition is tracked and written by the unit of work's
/// next commit: a repository never saves (analyzer MKP003), and every command runs in the unit of
/// work's transaction (ADR-ARCH-033 §4).
/// </summary>
internal sealed class ProfileRepository(AccessDbContext context) : IProfileRepository
{
    public async ValueTask<Profile?> FindByUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
        await context.Set<Profile>().SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken).ConfigureAwait(false);

    public ValueTask AddAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        context.Add(profile);
        return ValueTask.CompletedTask;
    }
}
