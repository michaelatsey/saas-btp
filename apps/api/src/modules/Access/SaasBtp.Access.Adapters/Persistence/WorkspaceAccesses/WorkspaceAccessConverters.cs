using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SaasBtp.Access.Domain.WorkspaceAccesses;

namespace SaasBtp.Access.Adapters.Persistence.WorkspaceAccesses;

/// <summary>Stores a <see cref="WorkspaceAccessId"/> as its UUID.</summary>
internal sealed class WorkspaceAccessIdConverter()
    : ValueConverter<WorkspaceAccessId, Guid>(id => id.Value, value => new WorkspaceAccessId(value));
