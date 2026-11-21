using MicroKit.Domain.Identifiers;

namespace SaasBtp.Access.Domain.Workspaces;

/// <summary>
/// The identifier of a workspace, a UUIDv7 (naming.md, "Aggregate identifiers").
/// </summary>
/// <param name="Value">The UUID.</param>
public readonly record struct WorkspaceId(Guid Value) : IEntityId
{
    /// <summary>A new identifier: a UUIDv7 whose timestamp is <paramref name="at"/>.</summary>
    /// <param name="at">The instant of the creation, read from the injected clock.</param>
    public static WorkspaceId New(DateTimeOffset at) => new(Guid.CreateVersion7(at));
}
