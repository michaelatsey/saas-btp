# ADR-ARCH-025 — Runtime secrets in Doppler, one project per role, delivered without a CLI in the image

Status: accepted
Date: 2026-10-17
Refs: issue #146; ADR-ARCH-022; ADR-ARCH-024.

## Context
The API and the migration runner need database credentials of different strength: the runner
holds a DDL role, the API must not. Each exists for staging and production. Doppler already
holds the workstation's secrets. ADR-ARCH-022 keeps every secret out of the images, and
ADR-ARCH-024 runs the runner in a GitHub Actions job and the API as a Coolify Docker Image
application.

What the providers offer, as of 2026-10-17:
- A Doppler service token gives read-only access to one config of one project. The free plan
  allows 10 projects.
- Coolify integrates Doppler natively: a resource links one secret-manager source, with a
  service token whose scope fixes the project and config. Coolify fetches the values at
  deployment or start, does not store them in its database, redacts them from deployment logs,
  and writes them to the dotenv file the container reads on the server. A change in Doppler
  applies at the next deployment or restart.
- `dopplerhq/secrets-fetch-action` fetches a config with a service token in a GitHub Actions
  step, exposes each secret as a step output, and masks the values in the logs.

## Decision
1. Doppler is the one store of runtime secrets, with one project per role: `saas-btp-api` and
   `saas-btp-migrator`, each with the configs `stg` and `prd`. A secret of one role never
   appears in the other role's project.
2. The API receives its secrets through Coolify's Doppler integration, with a service token
   scoped to the one config of its environment (`saas-btp-api` `stg` or `prd`).
3. The runner receives its connection string through `dopplerhq/secrets-fetch-action` in the
   deployment job, with a service token scoped to `saas-btp-migrator` `stg` or `prd`, stored as
   a secret of the GitHub Environment of that environment. The job passes the one value it needs
   to the container as `ConnectionStrings__Migrations`, explicitly.
4. No image contains the Doppler CLI, a service token or a secret.
5. The other deployment credentials stay where they are used: the GHCR pull token
   (ADR-ARCH-023) in Coolify's registry settings, the Coolify API token in the GitHub
   Environment. Every credential is recorded in Bitwarden when it is created.

## Consequences
- Four Doppler service tokens exist, one per role and environment; each reads one config and
  nothing else, and each is rotated on its own.
- The API's credential is on the deployment server, in the dotenv file Coolify writes; the
  runner's credential never is.
- A secret changed in Doppler reaches the API at its next deployment or restart, not at once.
- Two of the free plan's ten projects are used.
- GitHub masks the runner's secrets in the job logs, except values whose Doppler visibility is
  `unmasked`; that visibility is not used for credentials.

## Alternatives considered
- One Doppler project holding both roles: rejected. As branch configs of one environment, one
  role's config would inherit the other's root config secrets. As one environment per role and
  environment, the pairs would take all four environments of the free plan, and the boundary
  between roles would rest on naming inside one project.
- The Doppler CLI in the image (`doppler run`): rejected (ADR-ARCH-022); it puts a binary and a
  service token in the running container.
- Secrets typed into Coolify's environment variables: rejected; a second store next to Doppler,
  with no single place to rotate a credential.
- Syncing Doppler into GitHub repository secrets: rejected; the runner's credential would be
  readable by every workflow of the repository, not only by jobs of the protected environment.

## References
- Doppler docs, "Service tokens": read-only access to one config of one project.
- Doppler docs, "Branch configs": a branch config inherits from its environment's root config.
- Doppler pricing, Developer plan: 10 projects, 4 environments.
- Coolify docs (`coollabsio/coolify-docs` at `3b7c416`), "Secret managers" and "Using secret
  managers"; Coolify v4.4.6 source, `app/Support/RemoteSecretValueFormatter.php`.
- `dopplerhq/secrets-fetch-action` v2.0.0: inputs, outputs, masking.
