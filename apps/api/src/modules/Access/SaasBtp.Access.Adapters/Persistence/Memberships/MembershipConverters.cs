using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SaasBtp.Access.Domain.Memberships;

namespace SaasBtp.Access.Adapters.Persistence.Memberships;

/// <summary>Stores a <see cref="MembershipId"/> as its UUID.</summary>
internal sealed class MembershipIdConverter() : ValueConverter<MembershipId, Guid>(id => id.Value, value => new MembershipId(value));
