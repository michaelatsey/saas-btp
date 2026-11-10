# ADR-ARCH-036 — Workflow jobs run on a versioned GitHub-hosted runner label, never on a `-latest` label; a move to another Ubuntu release is a reviewed change

Status: accepted
Date: 2026-11-10
Refs: issue #159; ADR-ARCH-022; ADR-ARCH-030; ADR-ORG-002; ADR-ORG-004.

## Context
Every job of the repository's workflows ran on `ubuntu-latest`. GitHub moves that label from
Ubuntu 24.04 to 26.04 gradually between 2026-10-19 and 2026-11-19 (GitHub changelog,
2026-09-17; actions/runner-images#14748); during the move a job runs on either image from one
run to the next, with no change in the repository.

The move reached the repository on 2026-11-09 and broke every delivery to staging. The 26.04
image (20261101.186.1) runs Docker 29.4.2 with its default containerd image store, the 24.04
image (20261101.360.1) Docker 28.0.4 (actions/runner-images,
`images/ubuntu/Ubuntu2604-Readme.md` and `Ubuntu2404-Readme.md`). With the containerd store,
buildx loads a build through the OCI exporter and leaves the config digest out of its metadata,
which `publish-api-images.yml` read to prove the pushed image is the one it checked: the
publication refused every image, closed, and nothing was delivered. The pull request checks
could not show it: `api-images` writes no metadata file, and the publication runs from `main`
only (ADR-ARCH-030, Consequences).

A pull request that changes the label goes through the checks of the new image, and its
verification includes a delivery on `main`; a label that moves on its own goes through none.

## Decision
1. Every job of a workflow this repository defines runs on a GitHub-hosted runner label that
   names an operating system release, `ubuntu-26.04` today, never on a `-latest` label.
2. A move to another release is a change of its own: an issue, a pull request that moves every
   job together and fixes what breaks on the new image, and a verification that includes a
   delivery on `main` completed on the new image.
3. `ci-api-images.yml` runs on the label of `publish-api-images.yml`, as it copies its builder
   and build options (#150).

## Consequences
- The label fixes the Ubuntu release, not the tools on it: GitHub rebuilds the image about
  weekly, and Docker, Buildx, `gh`, `jq`, `curl` and the .NET SDKs change under the same label.
  Such a change can still alter a workflow's behaviour with no change in the repository. The
  deployment logs `gh --version` (ADR-ARCH-030), every job log names the image version, and
  the delivery on `main` after each push is the check that catches a break; the delivery
  workflows fail closed.
- Nothing in the repository moves the label: the Dependabot configuration updates action
  versions, not `runs-on`. A move is made by hand, before GitHub retires the release.
- A job no longer picks up a new release on its own.
- A new job in any workflow carries a versioned label.

## Alternatives considered
- `ubuntu-latest` once the 2026 rollout ends: rejected; the next move of the label would again
  change the image under every job, unreviewed, and could break delivery the same way.
- `ubuntu-24.04` until the publication is fixed: rejected; it defers the move without
  removing it, and the fix is known and small.
- Self-hosted runners with a fixed image: rejected; out of proportion with the repository's
  needs, and their maintenance would fall on the maintainer.

## References
- GitHub changelog, 2026-09-17, and actions/runner-images#14748: the move of `ubuntu-latest`
  to Ubuntu 26.04.
- actions/runner-images, `images/ubuntu/Ubuntu2604-Readme.md` (image 20261101.186.1) and
  `images/ubuntu/Ubuntu2404-Readme.md` (image 20261101.360.1), read 2026-11-10.
- docker/buildx v0.38.1, `build/opt.go` (`--load` switched to the `oci` exporter when the
  daemon supports the OCI importer, with `prefer-image-digest`) and `build/build.go` (the config
  digest removed from the response when `prefer-image-digest` is set), read 2026-11-10.
- Delivery runs 42624425426 to 42722038127, failed at "Push by digest" on `ubuntu-26.04`.
