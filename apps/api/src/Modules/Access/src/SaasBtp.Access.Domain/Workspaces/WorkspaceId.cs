using MicroKit.Domain.Identifiers;

namespace SaasBtp.Access.Domain.Workspaces;

/// <summary>
/// Strongly-typed identifier for the <see cref="Workspace"/> aggregate. Backed by a UUIDv7
/// (<c>Guid.CreateVersion7()</c>) — time-ordered, index-friendly ids.
/// </summary>
public readonly record struct WorkspaceId(Guid Value) : IEntityId
{
    // MicroKit's IEntityId exposes the boxed primitive; implemented explicitly, exactly
    // as MicroKit.Domain.Identifiers.GuidId does.
    object IEntityId.Value => Value;

    /// <summary>Creates a new <see cref="WorkspaceId"/> backed by a fresh UUIDv7.</summary>
    public static WorkspaceId New() => new(Guid.CreateVersion7());
}
