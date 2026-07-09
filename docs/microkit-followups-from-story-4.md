# MicroKit follow-ups from Story #4 (Access) integration

Status: OPEN. Tracked here (in SaaS BTP), to be ACTIONED in the MicroKit repo.
Target MicroKit branch: docs/findings-story-4-integration.

Story #4 (Access) was the first external product consuming MicroKit.Auth +
MicroKit.Tenancy from nuget.org. It surfaced the frictions below. These are the
residual MicroKit-side actions - they are NOT done. SaaS BTP already carries a local
workaround for each (see session 007-2026-07-08-access-scaffold-me.md); what remains
is to file/resolve them in the MicroKit repo on the branch above.

- [ ] Finding 2 - MicroKit.Auth.Supabase: ship an ASP.NET Core authentication scheme
      (AddSupabaseAuthentication(), or an AuthenticationHandler in
      MicroKit.Auth.AspNetCore) so consumers stop hand-writing the IJwtValidator
      bridge. SaaS BTP workaround: SupabaseAuthenticationHandler in Access.Infrastructure.

- [ ] Finding 3 - MicroKit.Tenancy: give TenantId a TypeConverter / IParsable so
      ConfigurationTenantStore binds a scalar guid ("Id": "<guid>") instead of the nested
      "Id": { "Value": "<guid>" } shape; add a README note. SaaS BTP workaround:
      appsettings seeds the dev tenant in the nested shape.

- [ ] Finding 4 - MicroKit.Tenancy: document in the README that an ITenantStore is
      mandatory even for pure claims resolution - a store miss is a failure by design
      (not an exception), rejecting a forged/unknown tenant_id. SaaS BTP already relies
      on this as security-by-design.

- [x] Finding 1 - MicroKit.Auth: SupabaseAuthOptions init-only vs Action<T> overload
      (CS8852). ALREADY FIXED in MicroKit.Auth 1.0.0-preview.3 (properties made
      settable). No action left; recorded only as provenance - Story #4 was the consumer
      that surfaced it.
