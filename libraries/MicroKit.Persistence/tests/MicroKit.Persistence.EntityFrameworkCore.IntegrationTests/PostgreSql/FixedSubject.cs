using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests.PostgreSql;

/// <summary>
/// A subject port with one fixed subject. Added here, not copied from MicroKit: every transaction
/// of a context the provider registers sets its subject first (ADR-ARCH-033 §4), so a test that
/// registers its context through <c>UsePostgreSQL</c> registers a subject too. These tests' databases
/// have no row level security; the subject's value is not what they prove.
/// </summary>
public sealed class FixedSubject : IUnitOfWorkSubject
{
    public static FixedSubject Instance { get; } = new();

    private FixedSubject()
    {
    }

    public Guid? Current { get; } = Guid.Parse("0193f3c1-7c2a-7d4e-9a51-3b1f6c2d8e00");
}
