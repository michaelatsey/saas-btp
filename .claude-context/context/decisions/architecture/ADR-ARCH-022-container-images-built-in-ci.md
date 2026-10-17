# ADR-ARCH-022 — Container images built in CI from multi-stage Dockerfiles, base images pinned by digest

Status: accepted
Date: 2026-10-17
Refs: issue #146; #147.

## Context
The API (`apps/api/src/hosts/SaasBtp.Api`) and the migration runner
(`apps/api/database/SaasBtp.Migrations`, ADR-ARCH-019) are deployed as two containers by
Coolify on one Hetzner VPS, which reports `x86_64`. The image tested on staging must be the one
that reaches production. Coolify builds on the deployment server by default; its GitHub Actions
guide describes the alternative, where Actions builds and pushes the image and Coolify deploys
it. The .NET SDK can also build an image without a Dockerfile (`dotnet publish
/t:PublishContainer`).

Coolify runs its HTTP health check inside the container with `curl` or `wget`. The official
`aspnet:10.0-noble` image ships neither (checked in the image on 2026-10-17) and sets no `USER`,
though it creates the non-root user `app` and exports its id as `APP_UID` (1654). The chiseled
`runtime:10.0-noble-chiseled` image has no shell and no package manager and already runs as
`APP_UID`.

`global.json` pins the SDK with `rollForward: latestPatch`, which accepts only patches within the
pinned feature band. Microsoft publishes official SDK images for some feature bands only: the
`10.0.107` pin had no matching image, which blocked this work until #147 moved it to `10.0.402`,
the SDK of `sdk:10.0-noble`. Release builds treat warnings as errors under
`AnalysisLevel=latest-recommended` (`Directory.Build.props`), so a build under another SDK than
the one CI uses can fail, or pass, differently.

## Decision
1. Images are built by GitHub Actions, never on the deployment server, for `linux/amd64` only.
   Local builds pass `--platform linux/amd64` as well.
2. Each deployable project has one multi-stage Dockerfile next to it. The build context is the
   repository root, filtered by a root `.dockerignore` that excludes everything and lets back in
   only what the Dockerfiles copy.
3. The build stage runs on the official `sdk:10.0-noble` image and copies `global.json`,
   `Directory.Build.props`, `Directory.Packages.props` and `.editorconfig` before restoring, so
   the image is compiled with the SDK, the build settings and the package versions CI uses. It
   publishes in `Release`.
4. The API runs on `aspnet:10.0-noble`, with `curl` added for the Coolify health check, as
   `USER $APP_UID`.
5. The migration runner runs on `runtime:10.0-noble-chiseled`, as `USER $APP_UID`, stated even
   though the base image already sets it.
6. Every base image is referenced by digest. The tag written next to the digest is informative
   only.
7. SDK band policy: `global.json` pins a feature band for which an official SDK image exists. A
   digest bump that moves the SDK image to another feature band moves the `global.json` pin in
   the same pull request; a pull request that moves the `global.json` pin to another band moves
   the SDK image digest with it. A digest bump within the same band needs no `global.json`
   change.
8. No image carries a secret: no secret in a build argument, an `ENV` instruction or a copied
   file. Secrets reach containers at run time (ADR-ARCH-025).

## Consequences
- The deployment server needs no SDK and does no build; the image deployed to production is
  byte-for-byte the one deployed to staging (ADR-ARCH-023).
- A base image changes only through a reviewed digest bump, so security fixes in the base
  images reach the published images only when a bump is raised. Until Dependabot's `docker`
  ecosystem reads these Dockerfiles (#131), bumps are raised by hand. The SDK digest is written
  in both Dockerfiles and moves in both at once. A bump that crosses a feature band breaks the
  build stage until `global.json` follows, which is the intended signal.
- `curl` adds a binary to the API image, installed from the Ubuntu archive at build time and not
  pinned to a version. The API moves to a chiseled image only once its health check needs
  neither `curl` nor `wget`.
- The chiseled runner has no shell: it is diagnosed from its exit code and its output only. It
  also has no GSSAPI library: Npgsql 10 tries GSSAPI encryption by default, logs that
  `libgssapi_krb5.so.2` cannot be loaded and falls back to a plain connection, which Npgsql
  documents as harmless. `GSS Encryption Mode=Disable` in the runner's connection string removes
  the line.
- The Dockerfiles copy only their own project. A project reference added to either host must be
  added to its Dockerfile and to the `.dockerignore` allow-list, or the image build fails. No
  pull request check builds the images yet, so such a failure shows at the next publish.
- The image build does not generate the OpenAPI document: CI checks it (ADR-ARCH-018), and
  generating it would start the application during the image build.
- Multi-architecture images would need a change of this decision.

## Alternatives considered
- Building on the deployment server (Coolify's default): rejected; each environment would
  rebuild the image, so the image tested on staging would not be the one deployed to production,
  and the server would carry the build load.
- SDK container publishing (`dotnet publish /t:PublishContainer`): rejected; it cannot add a
  package such as `curl` to the base image, and Dependabot's `docker` ecosystem (#131) reads base
  images from a Dockerfile.
- The Doppler CLI in the image (`doppler run`): rejected; it puts a secret-manager binary and a
  service token inside the running container, where Coolify's Doppler integration and a GitHub
  Actions step deliver the same secrets without either (ADR-ARCH-025).
- A chiseled image for the API: rejected for now; it has no `curl` or `wget` for the Coolify
  health check.
- Base images by tag only: rejected; a tag moves, so two builds of the same commit could differ.
- Leaving `global.json` out of the build stage: rejected; the image would compile with whatever
  SDK the image ships, not the one CI tests with.

## References
- Coolify docs (`coollabsio/coolify-docs` at `3b7c416`): "Build and deployment model" (builds
  on the server by default), the GitHub Actions guide, and "Health checks" (run inside the
  container, need `curl` or `wget`).
- Microsoft Learn, "global.json overview": `latestPatch` rolls forward to the latest patch of the
  pinned feature band only.
- Npgsql docs, "Security and encryption": GSS encryption mode `Prefer` by default from Npgsql 10,
  the `libgssapi_krb5.so.2` message, and `GSS Encryption Mode=Disable`.
- Docker docs, `.dockerignore`: exclusion and exception patterns.
