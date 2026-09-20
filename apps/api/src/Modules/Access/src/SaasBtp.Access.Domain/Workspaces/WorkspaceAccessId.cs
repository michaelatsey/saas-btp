using MicroKit.Domain.Identifiers;

namespace SaasBtp.Access.Domain.Workspaces;

/// <summary>
/// Strongly-typed identifier for a <see cref="WorkspaceAccess"/> edge. Backed by a UUIDv7
/// (<c>Guid.CreateVersion7()</c>) — time-ordered, index-friendly ids.
/// </summary>
/// <remarks>
/// The access edge is the ONLY internal component of the two Access aggregates that carries an
/// identity, because it is the only one that must survive its own revocation (Implementation Design
/// §3.5). Ownership and organization membership are facts defined by their values and carry none.
/// </remarks>
public readonly record struct WorkspaceAccessId(Guid Value) : IEntityId
{
    // MicroKit's IEntityId exposes the boxed primitive; implemented explicitly, exactly as
    // MicroKit.Domain.Identifiers.GuidId does.
    object IEntityId.Value => Value;

    /// <summary>Creates a new <see cref="WorkspaceAccessId"/> backed by a fresh UUIDv7.</summary>
    public static WorkspaceAccessId New() => new(Guid.CreateVersion7());
}
