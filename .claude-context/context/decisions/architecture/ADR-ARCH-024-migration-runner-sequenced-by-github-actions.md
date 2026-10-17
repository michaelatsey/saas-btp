# ADR-ARCH-024 — The migration runner runs in a GitHub Actions job before the API is deployed

Status: accepted
Date: 2026-10-17
Refs: issue #146; ADR-ARCH-019.

## Context
ADR-ARCH-019 runs the migration runner as a separate step before the application starts, and
requires every script to stay compatible with the application version still running. A
deployment must stop when the runner fails, and the runner's database credential (a DDL role)
must never reach the API container (ADR-ARCH-025). Both images are published by digest
(ADR-ARCH-023).

What Coolify offers, as of v4.4.6:
- A Compose application can order services with `service_completed_successfully`, but Coolify
  adds an `env_file: .env` holding every runtime variable of the resource to each service, a
  resource links one secret-manager source, and Compose applications get no rolling update.
- The pre-deployment command runs inside the currently running application container, so in the
  old image, and is skipped on the first deployment, when no container exists.
- A Docker Image application deploys a published image by tag or by digest, with a rolling
  update: the new container starts while the old one keeps serving, and is switched to once
  started or, with a health check, once healthy.
- The Coolify API updates an application's image (`PATCH /api/v1/applications/{uuid}`) and
  triggers a deployment (`POST /api/v1/deploy`, with a token holding the deploy ability).

## Decision
1. The API is a Coolify Docker Image application in each environment, deployed by digest.
2. A deployment is a GitHub Actions job, one per environment, that:
   1. runs the runner image by digest on the GitHub-hosted runner, with its connection string
      mapped explicitly to `ConnectionStrings__Migrations` and nothing else from the job's
      environment;
   2. on exit code 0 only, points the API application at the new digest through the Coolify
      API and triggers its deployment, which Coolify performs as a rolling update gated by the
      API's health check;
   3. on any other exit code, fails and leaves the API untouched.
3. The job runs in a GitHub Environment per Coolify environment. That GitHub Environment holds
   the migrator credential and the Coolify API token, and its deployment branch policy allows
   `main` only.
4. The job runs in one concurrency group per environment and never cancels a run in progress.
5. Expand/contract is mandatory, as ADR-ARCH-019 states: during the rolling update the previous
   API version serves on the new schema.
6. The database accepts connections from GitHub-hosted runners. IP allow-listing on the Supabase
   project is therefore not used until a self-hosted runner with a fixed address exists.

## Consequences
- The runner's credential lives in GitHub and the runner's process only; Coolify and the API
  container never see it.
- A failed migration stops the deployment before Coolify is touched; the running API keeps
  serving. A migration that succeeded is not rolled back when the API deployment then fails:
  expand/contract keeps the previous API version working on that schema.
- Rolling updates do not guarantee zero downtime, and are disabled by a published host port, a
  fixed container name or a custom IP on the API application; its configuration must avoid them.
- The Coolify API's image field for a digest is set from Coolify's source, not its
  documentation: the digest is passed as the tag `sha256-<hex>`, which the deployment turns into
  `<image>@sha256:<hex>`; the `sha256:` form is refused. The job that implements this decision
  proves it on staging before relying on it. Coolify's deploy endpoint accepts POST only.
- The database is reachable from any GitHub-hosted runner address; the credential, not the
  network, is the boundary.
- The job is built by the first staging deployment (#127) and triggered after CI by #145; #146
  publishes the images only.

## Alternatives considered
- A Compose application with the runner as a service the API waits on
  (`service_completed_successfully`): rejected; Coolify hands every runtime variable of the
  resource, the runner's credential included, to the API service, links one secret-manager
  source per resource, and has no rolling update for Compose.
- The pre-deployment command: rejected; it runs in the old container, so with the old scripts,
  and is skipped on the first deployment.
- A Compose `pre_start` hook: rejected for the same shared-environment reason as Compose.
- Running migrations at API startup: rejected by ADR-ARCH-019.
- A runner deployed as its own Coolify resource and triggered before the API: rejected; Coolify
  offers no way to make the API deployment wait on another resource's exit code, so the ordering
  would live in a script anyway.

## References
- Coolify docs (`coollabsio/coolify-docs` at `3b7c416`): Docker Image deployments (tag or
  digest), rolling updates, health checks, pre-deployment command, secret managers.
- Coolify v4.4.6 source: `routes/api.php` (`PATCH /applications/{uuid}`, `POST /deploy`, GET
  deploy answering 405), `app/Support/ValidationPatterns.php` (tag pattern),
  `ApplicationDeploymentJob.php` (digest tag handling, `.env` added to Compose services),
  `bootstrap/helpers/parsers.php` (`env_file` injection).
- GitHub docs, "Deployments and environments": deployment branch policies; environment secrets
  available only to jobs that reference the environment.
