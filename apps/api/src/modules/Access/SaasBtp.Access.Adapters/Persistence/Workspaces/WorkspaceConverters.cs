using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Adapters.Persistence.Workspaces;

/// <summary>Stores a <see cref="WorkspaceId"/> as its UUID.</summary>
internal sealed class WorkspaceIdConverter() : ValueConverter<WorkspaceId, Guid>(id => id.Value, value => new WorkspaceId(value));

/// <summary>
/// Stores a <see cref="WorkspaceName"/> as the measured name; reads it back through
/// <see cref="WorkspaceName.TryCreate"/>, the one way to build one.
/// </summary>
internal sealed class WorkspaceNameConverter()
    : ValueConverter<WorkspaceName, string>(name => name.Value, value => StoredWorkspaceName.Read(value));

/// <summary>Reads back a workspace name the API wrote, measured.</summary>
internal static class StoredWorkspaceName
{
    public static WorkspaceName Read(string stored) =>
        WorkspaceName.TryCreate(stored, out var name, out var defect)
            ? name
            : throw new InvalidOperationException($"A stored workspace name is not valid ({defect}): correct the row.");
}
