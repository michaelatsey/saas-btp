using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SaasBtp.Access.Domain.Profiles;

namespace SaasBtp.Access.Adapters.Persistence.Profiles;

/// <summary>Stores a <see cref="ProfileId"/> as its UUID.</summary>
internal sealed class ProfileIdConverter() : ValueConverter<ProfileId, Guid>(id => id.Value, value => new ProfileId(value));

/// <summary>Stores a <see cref="UserId"/>, the identity provider's identifier, as its UUID.</summary>
internal sealed class UserIdConverter() : ValueConverter<UserId, Guid>(id => id.Value, value => new UserId(value));
