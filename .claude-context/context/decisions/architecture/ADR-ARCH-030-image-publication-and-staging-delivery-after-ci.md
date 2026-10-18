# ADR-ARCH-030 — Image registry, tags, promotion and attestation, with publication and staging deployment after CI: attested digests only, in commit order

Status: accepted
Date: 2026-10-18
Supersedes: ADR-ARCH-029
Refs: issue #145; ADR-ARCH-029; ADR-ARCH-024; ADR-ARCH-025; ADR-ORG-004.

## Context
ADR-ARCH-022 builds the API and migration runner images in GitHub Actions. They need a registry
Coolify can pull from, a naming that ties each image to the commit it was built from, a way to
move an image from staging to production without building it again, and a way for anyone
holding a digest to check that this repository's publish workflow built it. ADR-ARCH-024 deploys
them to an environment with a GitHub Actions job: the runner by digest, then the Coolify API
application by digest. Until now both publication and deployment were started by hand, and a
deployment accepted any digest whose tag and revision label matched, an image published before
attestations included.

GitHub Container Registry (GHCR) is reached from Actions with the workflow's `GITHUB_TOKEN`. A
tag can be pushed again and then points to another image: GHCR offers no tag immutability.
Pulling a private image outside Actions needs a personal access token (classic) with
`read:packages`; GHCR does not accept fine-grained tokens. A new package cannot be assumed
private: the first publication created both packages public (#146).

GitHub artifact attestations sign an in-toto statement for a subject digest with a certificate
issued from the job's OIDC token, and store it in GitHub's attestations API, associated with the
repository. This repository is public, so the attestation is signed through the Sigstore
public-good instance, whose transparency log is publicly readable, while the packages stay
private. `actions/attest` can also push the attestation to the registry, as an untagged OCI
artifact and, when the registry does not answer the referrers API, an OCI index under the tag
`sha256-<hex of the image digest>`, rewritten each time another attestation is added. GitHub's
documentation does not say whether GHCR serves the referrers API. `sha256-<hex>` is also the tag
form ADR-ARCH-024 passes to Coolify for a digest.

The certificate carries the signer workflow, from the token's `job_workflow_ref` claim, and the
source ref, from its `ref` claim; `gh attestation verify` checks them against
`--signer-workflow` and `--source-ref`. `--signer-workflow` constrains the workflow path, not
its ref, and a copy of the workflow on another branch carries the same path. In a reusable
workflow, `job_workflow_ref` names the called workflow, while `ref`, `sha` and `workflow_ref`
describe the caller. The provenance predicate of `actions/attest` takes its workflow path from
`workflow_ref`, its builder from `job_workflow_ref`, and its ref and commit from `ref` and
`sha`: for a run from `main`, that commit is the head of `main` when the run started, not the
commit given as input and built.

Docker Build adds its own provenance attestation by default, stored as a manifest in an image
index, unless `--provenance=false` is passed. The default `docker` builder driver cannot push an
image by digest without a tag; the `docker-container` driver can.

GitHub Actions, as documented when this decision was taken:
- A `workflow_run` run starts when another workflow's run completes, whatever its event and
  conclusion, pull requests included. Its `branches` filter matches the triggering run's branch
  name. It runs the workflow file of the default branch, with `GITHUB_REF` and `GITHUB_SHA` of
  the default branch, not of the triggering run; the triggering run's commit, event,
  conclusion, branch, head repository and workflow path are in the event payload.
- A re-run keeps the `GITHUB_SHA` and `GITHUB_REF` of the original event, within 30 days.
- A reusable workflow (`workflow_call`) called with a local path comes from the same commit as
  its caller and sees the caller's `github` context. The caller's job permissions are a ceiling.
  It receives only the secrets the caller passes, which can name an environment secret; the
  called job that sets `environment` receives that environment's value.
- In a concurrency group, at most one job runs. By default one more waits, and a newer one
  cancels and replaces it; with `queue: max`, up to 100 wait. They start in the order they began
  waiting, which GitHub does not guarantee. The documentation does not say whether a called
  workflow's top-level `concurrency` applies; a job's does.

Publications of different commits run in parallel, so deployments can be queued out of commit
order. ADR-ORG-004 rests unattended work on verification: a deployment that bypasses CI or the
attestation breaks that chain.

## Decision
1. Images are published to private GHCR packages owned by the repository owner:
   `ghcr.io/michaelatsey/saas-btp-api` and `ghcr.io/michaelatsey/saas-btp-migrator`. Each image
   carries the label `org.opencontainers.image.source` with the repository URL, which links the
   package to the repository, and `org.opencontainers.image.revision` with the commit SHA. The
   visibility of every new package is checked, and set to private, right after its first
   publication.
2. An image has exactly one tag, `sha-<full 40-character commit SHA>`, and it designates a single
   `linux/amd64` image manifest. Only commits on `main` are published.
3. Publishing is the workflow `.github/workflows/publish-api-images.yml`, run from `main` with
   the commit SHA as input: called by the delivery workflow of point 14 once CI passes, or
   dispatched by hand, to publish a commit again. Its runs are in one concurrency group per
   commit and image, which cancels neither a running nor a waiting run (`queue: max`). It never
   pushes a tag that already exists.
4. Every published image carries a GitHub build provenance attestation, created by the run that
   built and pushed it, and by no other: a provenance describes the run that creates it. A run
   publishes in this order: build, check that the image runs as a non-root user, push by digest
   with no tag, attest that digest, create the tag. The tag is created only once the attestation
   has succeeded, so a `sha-<SHA>` tag always designates an attested digest.
5. A run that finds `sha-<SHA>` already published resolves the tag to a digest once, then
   requires the revision label of that digest to name the commit and the digest to carry an
   attestation that passes the verification policy. It never attests that digest. Without an
   attestation, the run fails and a newer commit of `main` is published instead.
6. The verification policy is: the attestation of the resolved digest, for this repository,
   signed by `publish-api-images.yml` (`--signer-workflow`) from `refs/heads/main`
   (`--source-ref`). This policy is the control, not the place the attestation is stored. The
   attestation proves which workflow and ref built the digest; the revision label, inside the
   attested digest, names the commit built, which the provenance does not. This decision is the
   policy's only source: a workflow that applies it cites this point.
7. Attestations are kept in GitHub's attestations API only, not pushed to GHCR.
8. Docker Build's own provenance and SBOM attestations are not produced: the published reference
   stays an image manifest, and its provenance is the GitHub attestation.
9. Images are built with a `docker-container` builder whose BuildKit image is pinned by digest.
10. Deployments reference an image by digest (`<image>@sha256:<digest>`), never by tag.
    Promoting from staging to production is deploying the same digest to production: no
    rebuild, no new tag, no new attestation.
11. There are no environment tags (`latest`, `staging`, `production`).
12. Coolify pulls with a classic personal access token limited to `read:packages`.
13. A deployment verifies, with the policy of point 6, the attestation of both digests it
    resolves, before the runner runs. Any verification failure refuses the deployment, which
    then leaves the database and the API untouched; telling a missing attestation from another
    failure only shapes the message. Manual and automatic deployments are verified alike.
14. `.github/workflows/deliver-api-staging.yml` chains publication and deployment after CI. It
    runs on `workflow_run` of `CI API`, completed, for the branch `main`, and starts anything
    only when the triggering run concluded with success, for a `push` event, on the branch
    `main` of this repository, from `.github/workflows/ci-api.yml`. It then calls, as reusable
    workflows from its own commit, `publish-api-images.yml` and, once that succeeds,
    `deploy-api-staging.yml`, both with the triggering run's head commit. Both called workflows
    keep their manual dispatch.
15. Deployments of an environment are in one concurrency group, set on the deployment job, that
    cancels neither a running nor a waiting deployment (`queue: max`). An automatic deployment,
    any deployment not dispatched by hand, never moves the environment to an older commit: it
    establishes the commit the environment runs as the revision label of the digest the API
    application points at, which must be a commit on `main`, and proceeds only when that commit
    is the requested one or an ancestor of it. Otherwise, or when that commit cannot be
    established, it fails before the runner runs, naming the rule, the commit requested and the
    commit the environment runs. A deployment dispatched by hand, a rollback included, is not
    held to this rule.

## Consequences
- Every push to `main` whose CI passes reaches staging with no manual step, through attested
  digests only; a failed CI run, or one on a pull request, publishes and deploys nothing. The
  staging Environment's branch policy still allows `main` only (ADR-ARCH-025). A `workflow_run`
  run's ref is `main`; that the policy accepts a called deployment job on that ref is not
  documented, and the first delivery run proves it.
- A delivery run that fails can be an expected refusal: a commit older than the one staging
  runs, refused by point 15 when publications finish out of order or when the CI run of an older
  commit is re-run. The log names the rule and both commits.
- After several merges, staging runs the latest commit whose CI passed, as long as no more than
  100 deployments wait at once.
- A rollback holds until the next automatic deployment of a commit that descends from its
  target: a newer merge, a deployment still waiting, or a re-run of the CI of the very commit
  rolled back from. The rule prevents moving back, not moving forward again.
- The commit an environment runs is read from the image its API application is configured
  with, which the deployment sets before Coolify deploys it. After a deployment that failed
  past that point, the configuration names a commit the environment may not run; the rule
  then compares against the configured commit.
- An environment whose current commit cannot be established, such as one never deployed by this
  job or whose API application was changed by hand, takes no automatic deployment until a
  deployment is dispatched by hand.
- Images published before attestations, `fb689dcbf002fa205896c0c8ea4c682eba88492d` included,
  stay in GHCR without one and can no longer be deployed: they are no rollback target. Their
  commits are not published again under their tag.
- What runs where is read from the deployments, which hold digests, not from the registry.
- A commit maps to one image per role, and a published tag never changes, so the tag is an
  audit trail from a digest back to its commit, and the attestation one from a digest back to
  the workflow and ref that built it.
- Anyone with read access to the repository's attestations can verify a digest with
  `gh attestation verify oci://<image>@<digest>`, given a registry login to resolve the image,
  and the policy of point 6. A verification that omits `--source-ref` accepts a build of the
  same workflow from any branch.
- The policy of point 6 is written in both the publish and the deployment workflows. Each copy
  cites point 6 and passes the same flags; nothing in the repository yet fails when the two
  differ.
- For a publication called by the delivery workflow, the provenance names
  `deliver-api-staging.yml` as its workflow, `publish-api-images.yml` as its builder, the
  `workflow_run` event, and the head of `main` when the delivery run started as its commit; the
  certificate names `publish-api-images.yml` as signer, so the policy of point 6 holds. The
  commit built is read from the revision label of the attested digest.
- A run that fails after the push (attestation, tag) leaves an untagged digest in the package. A
  new run builds and pushes again, so its digest may differ. Untagged digests count toward the
  retention rule, still to be decided when the number of versions matters.
- The pushed image is built twice from the same cache, once loaded for the non-root check and
  once pushed; the run fails before attesting when the two config digests differ.
- The BuildKit image is pinned in the workflow, out of Dependabot's reach (#131), so it is
  bumped by hand, like the base images today.
- The pull token can read every package of the account; it is a long-lived credential, recorded
  in Bitwarden on creation and rotated by hand.
- The workflow does not set package visibility: a new package, or a new image role, adds a
  manual visibility check right after its first publication.
- The tag designates an OCI image manifest, a media type the workflow sets explicitly and
  checks before attesting.
- The delivery workflow runs from `main` only, so it can only be proven once merged. The
  version of `gh` that verifies attestations comes with the runner image and is logged by each
  deployment.

## Alternatives considered
- Publish and deploy jobs added to `ci-api.yml`: rejected; a re-run runs the workflow of the
  commit re-run, so a commit older than this decision would be published and deployed without
  its checks, and every later change to them would not apply to older commits.
- Two `workflow_run` links, publication after CI and deployment after publication: rejected; a
  `workflow_run` run's head commit is the head of `main`, not the commit published, and a manual
  publication would start a deployment.
- The default concurrency queue (`queue: single`): rejected; a waiting deployment of a newer
  commit would be cancelled by an older one queued after it, leaving staging behind.
- Cancelling a deployment in progress: rejected by ADR-ARCH-024; it would leave staging half
  deployed.
- Relying on the queue's order: rejected; GitHub does not guarantee it, and publications finish
  in any order.
- Skipping a refused deployment with success instead of failing: rejected; every later step
  would need a guard, and one forgotten guard would deploy.
- Reading the commit an environment runs from GitHub's deployment records: rejected; they record
  the run's commit, the head of `main` for a `workflow_run` run, not the commit deployed.
- One shared script for the policy: rejected for now; the publish workflow checks out the commit
  it publishes, which for an older commit does not contain the script.
- Docker Hub or another registry: rejected; another account and credential, where GHCR is
  reached from Actions with the token every run already has.
- Environment tags such as `staging` or `latest`: rejected; a mutable tag hides which image ran,
  and a promotion becomes a retag that can race a deployment.
- A short SHA as tag: rejected; it can become ambiguous as the history grows.
- Building again for production: rejected; the image tested on staging would not be the one
  deployed (ADR-ARCH-022).
- Pushing the tag first and attesting after: rejected; a failed attestation would leave a
  published tag that can never be attested, since only the run that pushed a digest attests it.
- Attesting an already published digest from a later run: rejected; the provenance would
  describe a run that did not build it.
- Pushing attestations to GHCR (`push-to-registry`): rejected for now; nothing in this project
  verifies from the registry, it would add untagged versions and possibly a mutable
  `sha256-<hex>` referrers tag, the tag string ADR-ARCH-024 passes to Coolify for a digest, and
  GitHub's documentation does not say whether GHCR serves the referrers API. Reopened when a
  verifier reads attestations from the registry, or when verification must work without access
  to GitHub's API.
- Docker Build's provenance (`--provenance`): rejected; it makes the pushed reference an index,
  and nothing verifies it.
- `--signer-workflow` alone as the policy: rejected; it does not constrain the ref.
- `--source-digest` set to the input commit: rejected; the certificate carries the run's commit,
  so every publication of a commit other than the head of `main` at the run's start would fail.

## References
- GitHub docs (`github/docs` at `dae83f7d28f3b8e10015bd6b6d8b72267cedceca`, read 2026-10-18):
  "Events that trigger workflows" (`workflow_run`: `GITHUB_SHA` and `GITHUB_REF` of the default
  branch, any conclusion, workflow file on the default branch, three levels at most);
  `workflow_run` webhook payload (`conclusion`, `event`, `head_branch`, `head_sha`,
  `head_repository`, `path`); workflow syntax (`on.workflow_run.branches`, `on.workflow_call`);
  "Reusing workflow configurations" and "Reuse workflows" (same commit for a local path, caller's
  `github` context, permissions as a ceiling, environment secrets passed by name); concurrency
  (`queue: single` and `max`, order not guaranteed, job-level contexts); "Re-run workflows and
  jobs" (same `GITHUB_SHA` and `GITHUB_REF`, 30 days); "OpenID Connect reference" and "Using
  OpenID Connect with reusable workflows" (`job_workflow_ref` for the called workflow, other
  claims for the caller).
- GitHub REST API, workflow runs of `ci-api.yml` (read 2026-10-18): `path` is
  `.github/workflows/ci-api.yml`.
- Fulcio `config/identity/config.yaml` (`sigstore/fulcio` at `78da569`, read 2026-10-18):
  subject alternative name and Build Signer URI from `job_workflow_ref`, Source Repository Ref
  from `ref`, Build Config URI from `workflow_ref`.
- `actions/attest` v4.2.2 (`1e69f48acb82d1966a394da916b4c1698aa569d6`, read 2026-10-18):
  `buildSLSAProvenancePredicate` in `dist/index.js` (workflow path from `workflow_ref`, builder
  from `job_workflow_ref`, ref and commit from `ref` and `sha`); `src/main.ts` (Sigstore instance
  from the repository visibility); `src/attest.ts` (registry push only with `push-to-registry`).
- GitHub docs, "Working with the Container registry": the `org.opencontainers.image.source`
  label, classic tokens only, `read:packages`.
- GitHub docs, "Publishing Docker images": publishing to GHCR from Actions with `GITHUB_TOKEN`.
- GitHub docs, "Use artifact attestations" (`github/docs` at `1484a08`, read 2026-10-18):
  `subject-name` without tag, `subject-digest`, permissions `id-token: write`,
  `contents: read`, `attestations: write`, `packages: write`; verification of an image with
  `gh attestation verify oci://...` after a registry login.
- GitHub docs, "Using GitHub CLI in workflows" and the fine-grained permissions data
  (`github/docs`, read 2026-10-18): `GH_TOKEN` for each step running `gh`;
  `GET /repos/{owner}/{repo}/attestations/{subject_digest}` needs Attestations read.
- GitHub CLI v2.102.0 (read 2026-10-18): `pkg/cmd/attestation/verify/policy.go`
  (`--signer-workflow` matches the path with any ref), `verification/extensions.go` (exact source
  ref match), `api/client.go` (a 404 from the attestations API is not mapped to "no attestations
  found").
- Docker docs, "Image and registry exporters" and "Docker container driver" (read 2026-10-18):
  `push-by-digest`, `oci-mediatypes`, the `image` driver option. Docker Buildx v0.38.0:
  `build/opt.go` (push by digest refused on the docker driver),
  `util/imagetools/create.go` (single manifest source copied unchanged with
  `--prefer-index=false`).
- Docker docs, "Build attestations": provenance added by default, stored in the image index,
  disabled with `--provenance=false`.
