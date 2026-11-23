# ADR-ARCH-041 — The settings check of ADR-ARCH-038 runs in GitHub Actions with a scoped Management API token held by its own Doppler role and GitHub Environment

Status: accepted
Date: 2026-11-23
Refs: issue #225; #226 (staging configuration); parent #209 (BP-001); ADR-ARCH-025;
ADR-ARCH-026; ADR-ARCH-030; ADR-ARCH-036; ADR-ARCH-038; ADR-ORG-001; ADR-ORG-004.

## Context
ADR-ARCH-038 makes four settings of each Supabase project part of the API's token validation:
an ES256 or RS256 signing key in use (decision 1), e-mail confirmations required, phone
confirmations required, and the custom access token hook enabled on the versioned function
(decision 5). It requires a check that fails when one of them differs, tested on recorded
configurations (decision 9). Its Consequences leave to #138 where the check runs, which
fields it reads, and where its Management API credential lives: that credential is stronger
than anything the API or the runner holds, neither receives it, and it is placed under
ADR-ARCH-025 or under a new ADR if no role fits.

ADR-ARCH-025 keeps runtime secrets in Doppler, one project per role, `saas-btp-api` and
`saas-btp-migrator`, each with the configs `stg` and `prd`; the runner's service token is a
secret of the GitHub Environment `staging`, which the deployment job of
`deploy-api-staging.yml` references. Neither role fits a credential that reads the provider's
project settings. An accepted ADR is never edited, so this one adds the role.

What the providers document, read on 2026-11-23:

- **The Management API.** Base `https://api.supabase.com`; every request carries
  `Authorization: Bearer <token>`; a standard limit of 120 requests per minute, per user for a
  personal access token ("all tokens created by the same user share it") (reference,
  "Introduction").
  - `GET /v1/projects/{ref}/config/auth`, "Gets project's auth config", returns among others
    `mailer_autoconfirm` (boolean, nullable), `sms_autoconfirm` (boolean, nullable),
    `hook_custom_access_token_enabled` (boolean, nullable) and `hook_custom_access_token_uri`
    (string, nullable). The reference does not describe these fields.
  - `GET /v1/projects/{ref}/config/auth/signing-keys`, "List all signing keys for the
    project", returns `{"keys": [...]}`, each key with `id`, `algorithm` (`EdDSA`, `ES256`,
    `RS256`, `HS256`), `status` (`in_use`, `previously_used`, `revoked`, `standby`),
    `public_jwk`, `created_at` and `updated_at`.
- **What the fields mean.** The Supabase Auth server's README: "`MAILER_AUTOCONFIRM` - `bool`:
  If you do not require email confirmation, you may set this to `true`. Defaults to
  `false`", and the same for `SMS_AUTOCONFIRM` and phone confirmation. The provider's CLI maps
  its `auth.email.enable_confirmations` and `auth.sms.enable_confirmations` ("If enabled, users
  need to confirm ... before signing in", CLI "Configuration") to `mailer_autoconfirm` and
  `sms_autoconfirm` with `transform: invert` (`supabase/cli`
  `f9df34ff9acd406a5d7818efee6edd12fd2e8265`,
  `apps/cli/src/commands/config/push/push.encoders.ts`; `invertedBoolRow` in
  `packages/config/src/project-config/registry-auth.ts`), and each hook to
  `hook_<name>_enabled` and `hook_<name>_uri`. A required confirmation therefore reads `false`.
  A Postgres hook's URI is `pg-functions://postgres/<schema>/<function_name>` ("Auth Hooks").
- **Personal access tokens** ("Personal Access Tokens",
  `supabase.com/docs/guides/platform/personal-access-tokens`). "A scoped PAT can only reach the
  organizations, projects, and permissions you choose when creating it. A classic PAT, shown as
  Legacy in the dashboard, carries the same privileges as your user account. Prefer scoped
  PATs." Its permission table lists "Get auth service config" under **Auth Config**, Read, and
  "Get project signing keys" under **Auth Signing Keys**, Read. The page's source
  (`apps/docs/content/guides/platform/personal-access-tokens.mdx` in `supabase/supabase`)
  opened, from `2681a21f5c6f06836ff10b91eadb9f13cae3cae7` (2026-09-01), with a caution
  admonition: "Scoped personal access tokens are in **public alpha** and rolling out
  gradually. If you don't see the option to choose permissions when creating a token, your
  account doesn't have access yet." Commit `0eb08cb9f09fc24de96e00cf6cde4321cd639290`
  (2026-09-28, "docs: prepare scoped personal access tokens docs for GA") removed it. The page
  read on 2026-11-23, its `.md` rendering and its source at
  `b103d6098b788fe42c5dfa2e0f7d0964b7a2f10a`, holds no such admonition and names no release
  stage.
- **GitHub environments.** "Secrets stored in an environment are only available to workflow
  jobs that reference the environment" (`actions/reference/workflows-and-actions/
  deployments-and-environments`). "Running a workflow that references an environment that does
  not exist will create an environment with the referenced name", which "will not have any
  protection rules or secrets configured" (`actions/how-tos/deploy/
  configure-and-manage-deployments/manage-environments`), where "A job that references an
  environment must follow any protection rules for the environment before running or accessing
  the environment's secrets." Under "Deployment branches and tags", "Selected branches and
  tags": "Only branches and tags that match your specified name patterns can deploy to the
  environment" (`deployments-and-environments`). By default, "when a workflow job references an
  environment, GitHub creates a deployment object to track the deployment"; `deployment: false`
  opts out of it, and with it wait timers and required reviewers still apply, and "**Access
  control**—restrict which branches can use certain secrets via environment branch policies,
  without deployment tracking" is listed among its uses, but no sentence states that a branch
  policy is enforced for such a job
  (`actions/how-tos/deploy/configure-and-manage-deployments/control-deployments`).
- **GitHub REST.** `GET /repos/{owner}/{repo}/environments/{environment_name}` returns
  `deployment_branch_policy`, null or `{protected_branches, custom_branch_policies}`;
  `.../deployment-branch-policies` returns `branch_policies[]` with `name` and `type`
  (`branch`, `tag`). Both are listed under the "Actions" repository permission, read
  (`rest/authentication/permissions-required-for-fine-grained-personal-access-tokens`). A
  missing environment answers 404: observed on this repository, not stated on the page.
- **Schedules.** The `schedule` event "can be delayed during periods of high loads of GitHub
  Actions workflow runs. High load times include the start of every hour." Scheduled workflows
  run in UTC by default and on the default branch only; "In a public repository, scheduled
  workflows are automatically disabled when no repository activity has occurred in 60 days"
  (`actions/reference/workflows-and-actions/events-that-trigger-workflows`).

## Decision
1. **The credential.** The check authenticates with a scoped personal access token of the
   Supabase Management API, one per Supabase project, limited to that project, with
   **Auth Config** (Read) and **Auth Signing Keys** (Read) and no other permission: the two
   rows of the provider's permission table that hold "Get auth service config" and "Get
   project signing keys". A classic token, and any write permission, are refused. Scoped
   tokens were described by the provider as in public alpha and rolling out gradually until
   2026-09-28 (Context). If they are not available on the account, the check is not wired to
   CI until they are; it never falls back to a classic token.
2. **Its store: a third Doppler role.** Doppler project `saas-btp-settings-check`, configs
   `stg` and `prd`, under ADR-ARCH-025's rule of one project per role. Each config holds the
   scoped token of its Supabase project as `SUPABASE_ACCESS_TOKEN`, and that project's
   reference as `SUPABASE_PROJECT_REF`. Neither appears in the API's or the runner's project.
3. **Its delivery: a GitHub Environment of its own.** The GitHub Environment
   `settings-check-staging`, and `settings-check-production` once the maintainer creates it
   (ADR-ORG-004 §4), holds only the Doppler service token of its config, as
   `DOPPLER_SETTINGS_CHECK_TOKEN`, with deployment branches restricted to the branch `main`
   ("Selected branches and tags", one branch policy, `main`). No job of
   `deploy-api-staging.yml` or `deliver-api-staging.yml` references it, and no job that
   references it receives `DOPPLER_MIGRATOR_TOKEN` or `COOLIFY_API_TOKEN`: GitHub makes an
   environment's secrets available only to the jobs that reference it, so neither the API nor
   the runner can receive the token.
4. **Where it runs, and how it is started.** In the workflow
   `.github/workflows/check-supabase-auth-settings.yml`, on `ubuntu-26.04` (ADR-ARCH-036), with
   actions pinned by full commit SHA as the other workflows pin them:
   - by hand from `main` (`workflow_dispatch`), offering `staging` only. Production is added by
     its own change once the maintainer has created its Environment and credential; until then
     the workflow does not name `settings-check-production`;
   - every day against staging at 05:23 UTC, a minute off the hour, since GitHub documents
     delays at the start of every hour.

   The Environment a run uses is `settings-check-<environment>`, from the dispatch input, and
   `settings-check-staging` for the schedule.
5. **The environment gate.** Because a run that references a missing environment creates it
   without protection rules or secrets, a first job, which references no environment and
   holds only `actions: read` (the permission GitHub lists for reading an environment and its
   branch policies), reads the run's Environment, `settings-check-<environment>`, through the
   REST API. It fails, naming the Environment, when the run's ref is not `refs/heads/main`, when
   the Environment does not exist, on any answer other than 200 or one that is not a JSON
   object, or when its deployment branches are not exactly the branch `main`. The check job
   `needs` it, and references the Environment as a deployment environment
   (`environment: settings-check-<environment>`). Until the maintainer has created the Environment
   restricted to `main` (#226), every run fails at the gate, by design: it reports the
   Environment as missing or, once a run has created it, as not restricted to `main`. GitHub
   does not say at which point of a run a referenced environment is created.
6. **The branch boundary.** The boundary is the Environment's deployment branch policy, as
   GitHub documents it for a job that references the environment: the job follows the
   environment's protection rules before running or reading its secrets, and only the selected
   branches can deploy to it. `deployment: false` is not used, because GitHub does not state
   that the policy is enforced for a job that sets it. The gate and the check job each also
   refuse a ref other than `refs/heads/main` before anything else, which records the reason in
   the log; a dispatch from another branch runs that branch's copy of the workflow, so these
   checks are not the boundary. A dispatch from another branch, refused before any secret is
   available, confirms the policy once the Environment exists (#226).
7. **What the check reads, and when it fails.** The script
   `scripts/ci/check-supabase-auth-settings.sh` reads `GET /v1/projects/{ref}/config/auth` and
   `GET /v1/projects/{ref}/config/auth/signing-keys`, and fails, naming each setting that
   differs, unless:
   - exactly one key has `status` `in_use`, and its `algorithm` is `ES256` or `RS256`;
   - `mailer_autoconfirm` is `false`;
   - `sms_autoconfirm` is `false`;
   - `hook_custom_access_token_enabled` is `true` and `hook_custom_access_token_uri` is
     `pg-functions://postgres/access/custom_access_token_hook`, the function of
     `0004__access__create_access_token_hook.sql`.

   Comparisons are strict: a missing or null value, or one of another JSON type, differs.
8. **An answer that is not a success.** Any answer other than 200, no answer, or a body that is
   not a JSON object makes the check fail as unreadable, naming the endpoint and the status; it
   is never evaluated and never reported as a pass. A 200 answer is the project's stored
   configuration and is evaluated, whatever the project's state: what a paused project
   (ADR-ARCH-026) answers is not documented, and the token's two read permissions cannot read
   the project's status.
9. **The token in the job.** The check job holds no secret other than its Environment's
   Doppler service token, and read-only `permissions`. The Doppler action fetches the config
   without exporting it to the job's environment; only the step that runs the script receives
   the token, which reaches `curl` on its standard input, never on a command line, and is never
   printed.
10. **Custody.** Every token is recorded in Bitwarden when it is created; rotating it is the
    maintainer's. The maintainer creates the Doppler project, its configs, the scoped tokens,
    the service tokens and the Environments: for staging in #226, for production on their own
    (ADR-ORG-004 §4). No file of the repository holds a value of any of them.

## Consequences
- The settings check exists for staging once #226 has created the Doppler config, the
  Environment and the tokens. Until then the scheduled run fails every day at the gate, which
  names the Environment as missing or as not restricted to `main`; #226 restricts it if a run
  created it first.
- Three of the Doppler free plan's ten projects are used. ADR-ARCH-025 counts four service
  tokens by design, one per role and environment; this role adds two, one per config, six in
  total, the production one created by the maintainer.
- The check is tested on recorded configurations by
  `scripts/ci/check-supabase-auth-settings.test.sh` (decision 9 of ADR-ARCH-038), and its gate
  and the workflows' references by `scripts/ci/check-settings-check-environment.test.sh`. The
  workflow's `test` job runs both before every check. Pull request CI does not run them.
- The scoped token shares the maintainer's Management API rate limit with every other token of
  the same user; the check makes two requests per run.
- In a public repository (ADR-ORG-001), GitHub disables the schedule after 60 days without
  activity; the check then runs only when dispatched, until the schedule is enabled again.
- Detecting a paused staging project is not this check's: it evaluates what the provider
  answers.
- Each run of the check job creates a deployment record in the Environment's history; no
  permission is added for it, as the deployment job of `deploy-api-staging.yml` references
  `environment: staging` with read permissions only.
- The token lifetime (`jwt_exp`) is not among the four settings: the API enforces one hour
  itself (ADR-ARCH-038 decision 1).

## Alternatives considered
- **The token in the GitHub Environment `staging`.** Rejected: the deployment job references
  that Environment, so the job that runs the runner could read the token, against
  ADR-ARCH-038's Consequences.
- **A classic personal access token.** Rejected: it carries every permission of the
  maintainer's account on every organization and project, staging and production together.
- **The scoped token as a GitHub Environment secret, without Doppler.** Rejected: a second store
  of runtime secrets next to Doppler, which ADR-ARCH-025 keeps as the one store.
- **Running the check only from the maintainer's workstation.** Rejected: drift would be
  detected only when someone thinks of running it.
- **A job referencing the Environment without a gate.** Rejected: a run before the Environment
  exists would create it with no branch policy, and later runs would find it unrestricted.

## References
- Supabase documentation, read 2026-11-23: Management API reference "Introduction", "Gets
  project's auth config", "List all signing keys for the project"; "Personal Access Tokens"
  and its source in `supabase/supabase` at `2681a21f5c6f06836ff10b91eadb9f13cae3cae7`,
  `0eb08cb9f09fc24de96e00cf6cde4321cd639290` and `b103d6098b788fe42c5dfa2e0f7d0964b7a2f10a`;
  "Auth Hooks"; CLI "Configuration".
- Supabase Auth server, `supabase/auth`, `README.md` (`MAILER_AUTOCONFIRM`, `SMS_AUTOCONFIRM`),
  read 2026-11-23.
- Supabase CLI, `supabase/cli` `f9df34ff9acd406a5d7818efee6edd12fd2e8265`:
  `apps/cli/src/commands/config/push/push.encoders.ts`,
  `packages/config/src/project-config/registry-auth.ts`,
  `apps/cli-e2e/fixtures/recorded/GET_v1_projects_replayfixtureprojref_config_auth/default.response.json`.
- GitHub Docs, read 2026-11-23, under `docs.github.com/en/`:
  `actions/reference/workflows-and-actions/deployments-and-environments`;
  `actions/how-tos/deploy/configure-and-manage-deployments/manage-environments`;
  `actions/how-tos/deploy/configure-and-manage-deployments/control-deployments`;
  `actions/reference/workflows-and-actions/workflow-syntax` (`jobs.<job_id>.environment`,
  `permissions`); `actions/reference/workflows-and-actions/events-that-trigger-workflows`
  (`schedule`); `rest/deployments/environments`; `rest/deployments/branch-policies`;
  `rest/authentication/permissions-required-for-fine-grained-personal-access-tokens`.
