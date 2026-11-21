namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

/// <summary>
/// The subject of the current unit of work: the identifier every transaction of a context
/// registered by <see cref="PostgreSqlEfCoreBuilderExtensions.UsePostgreSQL{TContext}"/> sets in
/// <c>app.user_id</c> as its first statement, so that row level security sees the caller.
/// </summary>
/// <remarks>
/// <para>
/// Added in this repository (ADR-ARCH-035 §8): the host implements it from the token it has
/// validated (ADR-ARCH-033 §4). The subject is the token's <c>sub</c>, which is a UUID
/// (ADR-ARCH-038 decisions 1 and 3).
/// </para>
/// <para>
/// <see cref="Current"/> is <see langword="null"/> when the unit of work has no subject; a
/// transaction then fails to start, so no command runs without one.
/// </para>
/// <para>
/// Without <see cref="ProviderOptions.PoolSize"/>, the port is resolved from the scope that
/// resolves the context, with any lifetime. With <see cref="ProviderOptions.PoolSize"/> set, it is
/// resolved once from the root provider and shared by every lease, so it must be registered as a
/// singleton that reads the current unit of work at each call; any other lifetime is refused when
/// the first context is resolved.
/// </para>
/// </remarks>
public interface IUnitOfWorkSubject
{
    /// <summary>Gets the subject of the current unit of work, or <see langword="null"/> without one.</summary>
    Guid? Current { get; }
}
