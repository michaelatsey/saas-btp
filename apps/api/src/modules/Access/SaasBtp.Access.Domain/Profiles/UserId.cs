namespace SaasBtp.Access.Domain.Profiles;

/// <summary>
/// The identity provider's identifier of a person (ADR-ARCH-031): the person is external to this
/// domain and known only through it and their profile. Not an identifier of this domain.
/// </summary>
/// <param name="Value">The provider's user identifier.</param>
public readonly record struct UserId(Guid Value);
