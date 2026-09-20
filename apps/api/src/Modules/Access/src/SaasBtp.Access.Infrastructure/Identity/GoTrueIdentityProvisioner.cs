using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Result;
using SaasBtp.Access.Application.Features.FoundCompanySpace;

namespace SaasBtp.Access.Infrastructure.Identity;

/// <summary>
/// Establishes the founder's authenticated identity against the GoTrue Admin API
/// (<c>POST /auth/v1/admin/users</c>), with deterministic orphan adoption (ADR-ARCH-011).
/// </summary>
/// <remarks>
/// Authentication is the service-role key on the <c>apikey</c> header ALONE — adding
/// <c>Authorization: Bearer</c> makes GoTrue reject the call as an invalid JWT (measured, #48 spike).
/// The header is set once on the typed client at composition, never here.
/// <para>
/// There is NO distributed transaction and none is attempted. If the business transaction later
/// fails, the created auth user is left orphaned: harmless by design, and deterministically ADOPTED
/// by the next attempt. Compensating (deleting the auth user) is explicitly forbidden — it would race
/// a concurrent retry and delete an account about to be used.
/// </para>
/// </remarks>
/// <param name="client">The typed client, based at the Supabase project URL with the apikey header.</param>
internal sealed class GoTrueIdentityProvisioner(HttpClient client) : IIdentityProvisioner
{
    private const string AdminUsers = "auth/v1/admin/users";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public async ValueTask<Result<ProvisionedIdentity>> EstablishAsync(
        Email email, string fullName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        using var response = await client
            .PostAsJsonAsync(
                AdminUsers,
                // email_confirm marks the address verified at creation. SECURITY: BP-001 has no token
                // proving the founder controls it (unlike invitation acceptance) — the guard for that
                // is an OPEN DECISION, and the capability must not be exposed anonymously until it is
                // taken. See the identity-bootstrap gap in the BP-001 plan.
                new CreateUserRequest(email.Value, EmailConfirm: true, new UserMetadata(fullName)),
                Json,
                ct)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content
                .ReadFromJsonAsync<GoTrueUser>(Json, ct).ConfigureAwait(false);

            return created is null || created.Id == Guid.Empty
                ? Failure("the identity provider returned no user id")
                : Result.Success(new ProvisionedIdentity(created.Id, Adopted: false));
        }

        // 422 / email_exists is the ONLY branch that may adopt: it proves an account already holds
        // this address (a fully-onboarded person, or an orphan from an attempt that died before the
        // business transaction committed). Every other status is a plain failure.
        if (response.StatusCode is not HttpStatusCode.UnprocessableEntity)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Failure($"createUser failed with {(int)response.StatusCode}: {Truncate(body)}");
        }

        return await AdoptExistingAsync(email, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the existing account behind a 422 and adopts it — but only on an EXACT match.
    /// </summary>
    /// <remarks>
    /// SECURITY-CRITICAL. The Admin API <c>filter</c> parameter matches on a PREFIX (proven by the
    /// #48 spike: <c>throwaway+orphan</c> matched a full address), so the result set may contain
    /// addresses that merely start with the one we asked for. Taking <c>.First()</c> would attach the
    /// founding facts — ownership, membership, workspace access — to the WRONG PERSON. That is an
    /// impersonation, not a functional bug. Exactly one match adopts; zero fails; more than one
    /// aborts loudly and adopts nothing.
    /// </remarks>
    private async ValueTask<Result<ProvisionedIdentity>> AdoptExistingAsync(
        Email email, CancellationToken ct)
    {
        var query = $"{AdminUsers}?filter={Uri.EscapeDataString(email.Value)}";

        using var lookup = await client.GetAsync(query, ct).ConfigureAwait(false);
        if (!lookup.IsSuccessStatusCode)
            return Failure($"the existing-identity lookup failed with {(int)lookup.StatusCode}");

        var page = await lookup.Content
            .ReadFromJsonAsync<GoTrueUserList>(Json, ct).ConfigureAwait(false);

        var matches = (page?.Users ?? [])
            .Where(user => !string.IsNullOrWhiteSpace(user.Email))
            // Compare through the value object so the lookup uses the SAME normalization as the
            // write path — a prefix hit on a different address can never pass as equal.
            .Where(user => new Email(user.Email!) == email)
            .ToArray();

        return matches.Length switch
        {
            1 => Result.Success(new ProvisionedIdentity(matches[0].Id, Adopted: true)),
            0 => Failure("the identity provider reported the address exists but returned no exact match"),
            _ => Result.Failure<ProvisionedIdentity>(
                new FoundCompanySpaceErrors.AmbiguousFounderIdentity()),
        };
    }

    private static Result<ProvisionedIdentity> Failure(string reason) =>
        Result.Failure<ProvisionedIdentity>(
            new FoundCompanySpaceErrors.IdentityNotEstablished(reason));

    // Provider error bodies are echoed into our failure message: cap them so a hostile or verbose
    // response cannot bloat logs or a client payload.
    private static string Truncate(string body) =>
        string.IsNullOrWhiteSpace(body) ? "(empty body)"
        : body.Length <= 300 ? body
        : body[..300] + "…";

    private sealed record CreateUserRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("email_confirm")] bool EmailConfirm,
        [property: JsonPropertyName("user_metadata")] UserMetadata UserMetadata);

    private sealed record UserMetadata(
        [property: JsonPropertyName("full_name")] string FullName);

    private sealed record GoTrueUser(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("email")] string? Email);

    private sealed record GoTrueUserList(
        [property: JsonPropertyName("users")] IReadOnlyList<GoTrueUser>? Users);
}
