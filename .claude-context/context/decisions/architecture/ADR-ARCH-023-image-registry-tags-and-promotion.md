# ADR-ARCH-023 — Image registry, tags and promotion: private GHCR, one tag per commit, deployment by digest

Status: superseded by ADR-ARCH-029
Date: 2026-10-17
Refs: issue #146.

## Context
ADR-ARCH-022 builds the API and migration runner images in GitHub Actions. They need a registry
Coolify can pull from, a naming that ties each image to the commit it was built from, and a way
to move an image from staging to production without building it again.

GitHub Container Registry (GHCR) is reached from Actions with the workflow's `GITHUB_TOKEN`. A
package is private when first published. A tag can be pushed again and then points to another
image: GHCR offers no tag immutability. Pulling a private image outside Actions needs a personal
access token (classic) with `read:packages`; GHCR does not accept fine-grained tokens. Docker
Build adds a provenance attestation to images by default, attached as a manifest in an image
index, unless `--provenance=false` is passed.

## Decision
1. Images are published to private GHCR packages owned by the repository owner:
   `ghcr.io/michaelatsey/saas-btp-api` and `ghcr.io/michaelatsey/saas-btp-migrator`. Each image
   carries the label `org.opencontainers.image.source` with the repository URL, which links the
   package to the repository, and `org.opencontainers.image.revision` with the commit SHA.
2. An image has exactly one tag, `sha-<full 40-character commit SHA>`. Only commits on `main` are
   published.
3. Publishing is the manual workflow `.github/workflows/publish-api-images.yml`, run from `main`
   with the commit SHA as input. It never pushes a tag that already exists: an image already
   published from the same commit (its revision label) is reported instead, any other image
   under the tag fails the run. It runs in one concurrency group per commit.
4. Deployments reference an image by digest (`<image>@sha256:<digest>`), never by tag.
   Promoting from staging to production is deploying the same digest to production: no rebuild,
   no new tag.
5. There are no environment tags (`latest`, `staging`, `production`).
6. Coolify pulls with a classic personal access token limited to `read:packages`.
7. Images are published without a provenance or SBOM attestation for now: the pushed reference
   is a single `linux/amd64` image manifest.

## Consequences
- What runs where is read from the deployments, which hold digests, not from the registry.
- A commit maps to one image per role, and a published tag never changes, so the tag is an
  audit trail from a digest back to its commit.
- The pull token can read every package of the account; it is a long-lived credential, recorded
  in Bitwarden on creation and rotated by hand.
- Package visibility is checked after the first publish: the workflow does not set it.
- Published versions accumulate; a retention rule is decided when their number matters.
- An attestation can be added later without changing the tag or digest rules.

## Alternatives considered
- Docker Hub or another registry: rejected; another account and credential, where GHCR is
  reached from Actions with the token every run already has.
- Environment tags such as `staging` or `latest`: rejected; a mutable tag hides which image ran,
  and a promotion becomes a retag that can race a deployment.
- A short SHA as tag: rejected; it can become ambiguous as the history grows.
- Building again for production: rejected; the image tested on staging would not be the one
  deployed (ADR-ARCH-022).
- A provenance attestation now: deferred; nothing verifies it yet, and it makes the pushed
  reference an index rather than an image manifest.

## References
- GitHub docs, "Working with the Container registry": default private visibility, the
  `org.opencontainers.image.source` label, classic tokens only, `read:packages`.
- GitHub docs, "Publishing Docker images": publishing to GHCR from Actions with `GITHUB_TOKEN`.
- Docker docs, "Build attestations": provenance `mode=min` added by default, stored in the image
  index, disabled with `--provenance=false`.
