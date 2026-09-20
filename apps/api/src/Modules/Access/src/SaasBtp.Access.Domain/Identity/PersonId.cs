using MicroKit.Domain.Identifiers;

namespace SaasBtp.Access.Domain.Identity;

/// <summary>
/// The identity of a human being, as the Access domain names them. It is the identity the auth
/// boundary established — the domain receives it, and never mints one.
/// </summary>
/// <remarks>
/// It belongs to NEITHER aggregate: a person is not a part of an organization nor of a workspace,
/// they are the far end of every relation both aggregates hold. Placing it inside one of the two
/// aggregate folders would make the other's dependency look like a cross-aggregate reference, which
/// it is not — hence its own folder, for the shared identity vocabulary.
/// <para>
/// There is deliberately NO <c>Profile</c> type and no <c>ProfileId</c> in this domain.
/// <c>access.profiles</c> is an infrastructure projection of <c>auth.users</c> — GoTrue owns the
/// identity and the projection only mirrors it, so it is not a business concept and this model does
/// not carry it (Design Package §0). Its port and its carrier type live in the Application layer.
/// </para>
/// <para>
/// Unlike <c>OrganizationId</c> / <c>WorkspaceId</c> / <c>WorkspaceAccessId</c>, this identifier has
/// no <c>New()</c>: minting a person is the auth boundary's act, never the domain's. The absence of
/// that factory is the statement.
/// </para>
/// </remarks>
/// <param name="Value">The authenticated identity's value (equals <c>auth.users.id</c>).</param>
public readonly record struct PersonId(Guid Value) : IEntityId
{
    // MicroKit's IEntityId exposes the boxed primitive; implemented explicitly, exactly as
    // MicroKit.Domain.Identifiers.GuidId does.
    object IEntityId.Value => Value;

    /// <summary>Whether this identifier names nobody — the absence of an established identity.</summary>
    public bool IsUnidentified => Value == Guid.Empty;
}
