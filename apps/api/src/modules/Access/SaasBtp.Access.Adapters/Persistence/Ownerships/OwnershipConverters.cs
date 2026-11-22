using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SaasBtp.Access.Domain.Ownerships;

namespace SaasBtp.Access.Adapters.Persistence.Ownerships;

/// <summary>Stores an <see cref="OwnershipId"/> as its UUID.</summary>
internal sealed class OwnershipIdConverter() : ValueConverter<OwnershipId, Guid>(id => id.Value, value => new OwnershipId(value));
