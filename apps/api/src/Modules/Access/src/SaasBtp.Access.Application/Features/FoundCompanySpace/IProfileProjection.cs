namespace SaasBtp.Access.Application.Features.FoundCompanySpace;

/// <summary>
/// Writes the identity projection (<c>access.profiles</c>) that the founding relations attach the
/// person through.
/// </summary>
/// <remarks>
/// This is an APPLICATION port, not a repository, and the distinction is the whole point: a
/// repository is the collection of an AGGREGATE, and the projection is explicitly NOT a business
/// fact — it is infrastructure the business facts stand on (Design Package §0). Giving it a Domain
/// repository would smuggle an infrastructure concern into the domain model; leaving the Application
/// to talk to EF directly would break persistence ignorance. A narrow application port is what is
/// left, and it is legitimate for exactly that reason.
/// <para>
/// The write is enrolled in the SAME unit of work as the five business facts, so the projection can
/// never persist without them (Design Package §1, rupture points).
/// </para>
/// </remarks>
public interface IProfileProjection
{
    /// <summary>Enrols the founder's identity projection in the current unit of work.</summary>
    /// <param name="profile">The projection to write.</param>
    void Add(FounderProfile profile);
}
