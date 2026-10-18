# ADR-ARCH-029 — Image registry, tags, promotion and attestation: private GHCR, one tag per commit on an attested digest, deployment by digest

Status: superseded by ADR-ARCH-030
Date: 2026-10-18
Supersedes: ADR-ARCH-023
Refs: issue #151; ADR-ARCH-023; ADR-ARCH-024.

## Context
ADR-ARCH-022 builds the API and migration runner images in GitHub Actions. They need a registry
Coolify can pull from, a naming that ties each image to the commit it was built from, a way to
move an image from staging to production without building it again, and a way for anyone
holding a digest to check that this repository's publish workflow built it.

GitHub Container Registry (GHCR) is reached from Actions with the workflow's `GITHUB_TOKEN`. A
tag can be pushed again and then points to another image: GHCR offers no tag immutability.
Pulling a private image outside Actions needs a personal access token (classic) with
`read:packages`; GHCR does not accept fine-grained tokens. The first run of the publish workflow
created both packages public, and the maintainer set them private by hand (#146): a new package
cannot be assumed private.

GitHub artifact attestations sign an in-toto statement for a subject digest with a certificate
issued from the job's OIDC token, and store it in GitHub's attestations API, associated with the
repository. This repository is public, so the attestation is signed through the Sigstore
public-good instance, whose transparency log is publicly readable, while the packages stay
private. `actions/attest` can also push the attestation to the registry: an untagged OCI
artifact whose subject is the image and, when the registry does not answer the referrers API, an
OCI index under the tag `sha256-<hex of the image digest>`, rewritten each time another
attestation is added. GitHub's documentation does not say whether GHCR serves the referrers API.
`sha256-<hex>` is also the tag form ADR-ARCH-024 passes to Coolify for a digest.

The provenance predicate records the run's ref and the run's commit, both taken from the OIDC
token. For a manual run from `main`, that commit is the head of `main` at dispatch time, not
the commit given as input and built. The certificate carries the signer workflow (path and ref)
and the source ref; `gh attestation verify` checks them against `--signer-workflow` and
`--source-ref`. `--signer-workflow` constrains the workflow path, not its ref, and a copy of the
workflow on another branch carries the same path.

Docker Build adds its own provenance attestation by default, stored as a manifest in an image
index, unless `--provenance=false` is passed. The default `docker` builder driver cannot push an
image by digest without a tag; the `docker-container` driver can.

## Decision
1. Images are published to private GHCR packages owned by the repository owner:
   `ghcr.io/michaelatsey/saas-btp-api` and `ghcr.io/michaelatsey/saas-btp-migrator`. Each image
   carries the label `org.opencontainers.image.source` with the repository URL, which links the
   package to the repository, and `org.opencontainers.image.revision` with the commit SHA. The
   visibility of every new package is checked, and set to private, right after its first
   publication.
2. An image has exactly one tag, `sha-<full 40-character commit SHA>`, and it designates a single
   `linux/amd64` image manifest. Only commits on `main` are published.
3. Publishing is the manual workflow `.github/workflows/publish-api-images.yml`, run from `main`
   with the commit SHA as input, in one concurrency group per commit. It never pushes a tag that
   already exists.
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
   attested digest, names the commit built, which the provenance does not.
7. Attestations are kept in GitHub's attestations API only, not pushed to GHCR.
8. Docker Build's own provenance and SBOM attestations are not produced: the published reference
   stays an image manifest, and its provenance is the GitHub attestation.
9. Images are built with a `docker-container` builder whose BuildKit image is pinned by digest.
10. Deployments reference an image by digest (`<image>@sha256:<digest>`), never by tag.
    Promoting from staging to production is deploying the same digest to production: no
    rebuild, no new tag, no new attestation.
11. There are no environment tags (`latest`, `staging`, `production`).
12. Coolify pulls with a classic personal access token limited to `read:packages`.
13. Verifying the attestation before a deployment is deferred to the deployment job (#145,
    ADR-ARCH-024), which owns the decision to deploy a digest; this decision covers publication
    only.

## Consequences
- What runs where is read from the deployments, which hold digests, not from the registry.
- A commit maps to one image per role, and a published tag never changes, so the tag is an
  audit trail from a digest back to its commit, and the attestation one from a digest back to
  the workflow and ref that built it.
- Anyone with read access to the repository's attestations can verify a digest with
  `gh attestation verify oci://<image>@<digest>`, given a registry login to resolve the image,
  and the policy of point 6. A verification that omits `--source-ref` accepts a build of the
  same workflow from any branch.
- The provenance names the commit at the head of `main` when the run was dispatched, which can
  differ from the commit built; the commit built is read from the revision label of the attested
  digest.
- A run that fails after the push (attestation, tag) leaves an untagged digest in the package. A
  new run builds and pushes again, so its digest may differ. Untagged digests count toward the
  retention rule, still to be decided when the number of versions matters.
- Until the deployment job verifies attestations (point 13), a deployment accepts any digest
  whose tag and revision label match: an image published before attestations, or a tag written
  by any other writer to the package, such as a copy of the publish workflow on another branch
  without its checks. The guarantee of point 4 holds for this workflow run from `main`, not for
  every writer to the package.
- Images published before attestations, `fb689dcbf002fa205896c0c8ea4c682eba88492d` included,
  stay in GHCR without one. A run for their commit fails; their commits are not published again
  under their tag.
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

## Alternatives considered
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
  so every publication of a commit other than the head of `main` at dispatch would fail.

## References
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
- `actions/attest` v4.2.2 (`1e69f48acb82d1966a394da916b4c1698aa569d6`, read 2026-10-18):
  `action.yml`, `src/main.ts` (Sigstore instance from the repository visibility), `src/attest.ts`
  (registry push and storage record only with `push-to-registry`), and
  `buildSLSAProvenancePredicate` in `dist/index.js` (ref and commit from the OIDC claims).
- GitHub CLI v2.102.0, the version on `ubuntu-latest` image 20261012 (read 2026-10-18):
  `pkg/cmd/attestation/verify/policy.go` (`--signer-workflow` matches the path with any ref),
  `verification/extensions.go` (exact source ref match), `api/client.go` (a 404 from the
  attestations API is not mapped to "no attestations found").
- Fulcio `config/identity/config.yaml` and `docs/oid-info.md` (read 2026-10-18): Source
  Repository Ref from the token's `ref` claim, Build Signer URI from `job_workflow_ref`.
- Docker docs, "Image and registry exporters" and "Docker container driver" (read 2026-10-18):
  `push-by-digest`, `oci-mediatypes`, the `image` driver option. Docker Buildx v0.38.0:
  `build/opt.go` (push by digest refused on the docker driver),
  `util/imagetools/create.go` (single manifest source copied unchanged with
  `--prefer-index=false`).
- Docker docs, "Build attestations": provenance added by default, stored in the image index,
  disabled with `--provenance=false`.
