# ADR-ARCH-039 — The API image checks its health with a .NET probe instead of curl; Coolify's dashboard check runs the same probe as a command

Status: accepted
Date: 2026-11-11
Refs: issue #205; ADR-ARCH-020; ADR-ARCH-022; ADR-ARCH-024; ADR-ARCH-030.

## Context
ADR-ARCH-022 §4 runs the API on `aspnet:10.0-noble` "with `curl` added for the Coolify health
check": Coolify's dashboard HTTP check runs `curl` or `wget` inside the container, and the base
image ships neither. The final stage of the API Dockerfile therefore ran `apt-get update` and
installed `curl` from the Ubuntu archive at every build. On 2026-11-11, `ci-api-images.yml`, a
required check on `main`, failed three times on a documentation-only pull request (#204) because
`apt-get update` could not resolve `archive.ubuntu.com` or `security.ubuntu.com`, while the base
images from `mcr.microsoft.com` were pulled: an outage of a package archive blocked every merge
and every delivery to staging.

The final image already holds the .NET runtime. The API maps `/health/live`, which runs no check,
and `/health/ready`, which runs the database readiness check (`Program.cs`). Coolify's check on
staging, which gates the rolling update (ADR-ARCH-024 §2.2), is an HTTP GET of `/health/ready` on
port 8080 expecting 200, with interval 5 s, timeout 5 s, 10 retries and start period 5 s.

Docker's HEALTHCHECK reference defines the exit codes of a health check: "0: success - the
container is healthy and ready for use", "1: unhealthy - the container isn't working correctly",
"2: reserved - don't use this exit code". A run that takes longer than `--timeout` counts as a
failure, and only the last HEALTHCHECK of a Dockerfile takes effect.

The staging API is a Coolify Docker Image application (ADR-ARCH-024 §1), deployed as a rolling
update gated by its health check (ADR-ARCH-024 §2.2). Coolify's documentation says that "for a
non-Compose application, Coolify uses this instruction" (the image's HEALTHCHECK) "only while the
dashboard Healthcheck is disabled", that an enabled dashboard check replaces it, and that a
rolling update waits for the replacement's check "when a health check is configured in Coolify or
the image". Its Docker Image page does not mention HEALTHCHECK. Coolify's source decides it, read
at v4.4.6 (the version ADR-ARCH-024 read) and at v4.7.5 (the latest release, 2026-11-10), in
`app/Jobs/ApplicationDeploymentJob.php` and `app/Models/Application.php`:
- `health_check()`, which `rolling_update()` runs before stopping the running container, returns
  at once, the new container taken as healthy, when the dashboard check is disabled and
  `custom_healthcheck_found` is false; otherwise it polls Docker's
  `.State.Health.Status` of the new container until `healthy` or `unhealthy`.
- `custom_healthcheck_found` is set only by `parseHealthcheckFromDockerfile()`, which runs only
  for the `dockerfile` build pack or an application with an inline Dockerfile: never for a Docker
  Image application.
- `generate_healthcheck_commands()` runs, for the dashboard type `cmd`, the command entered in the
  dashboard, accepted when it matches `^[a-zA-Z0-9 \-_.\/:=@,+]+$` and is at most 1000 characters;
  for the type `http`, `curl ... || wget ... || exit 1`. The compose entry is a `CMD-SHELL` test,
  run through the image's `/bin/sh`.

For a Docker Image application, disabling the dashboard check therefore removes the gate, and the
image's HEALTHCHECK is never what Coolify waits on.

ASP.NET Core sets the HTTP endpoint from several sources. `ASPNETCORE_HTTP_PORTS`, which the base
image sets to 8080, is "a semicolon-delimited list of port values"; `URLS` overrides it, and so
does Kestrel's endpoint configuration; for `WebApplicationBuilder`, command-line arguments and
`DOTNET_`-prefixed variables take precedence over `ASPNETCORE_`-prefixed ones, and `URLS` is read
from app configuration, which unprefixed variables and app settings files feed (Microsoft Learn).

## Decision
1. **No package in the API image.** The final stage of the API Dockerfile installs nothing. The
   API still runs on `aspnet:10.0-noble`, by digest, as `USER $APP_UID` (ADR-ARCH-022 §4 without
   its `curl`).
2. **The probe.** `apps/api/src/hosts/SaasBtp.Api.HealthProbe` is a console project that runs on
   the base framework alone: SDK `Microsoft.NET.Sdk`, no `FrameworkReference`,
   `PackageReference` or `ProjectReference`, so it never depends on ASP.NET Core. It sends one
   `GET` to `http://127.0.0.1:<port>/health/ready`, the endpoint the staging check requests, which
   runs the database readiness check, where `<port>` is the first entry of
   `ASPNETCORE_HTTP_PORTS` as the host splits it (entries trimmed, empty ones dropped), with no
   proxy, so that a proxy variable never takes the request off
   the loopback, and without following redirects. It waits 3 seconds at most. It exits 0 on a
   2xx and 1 on anything else, an unreadable port, a refused connection, a timeout or an
   unexpected exception included, writing the cause on one line of standard error, which Docker
   keeps in the health log. It never exits 2.
3. **One source for the HTTP endpoint.** The API's HTTP endpoint is set through
   `ASPNETCORE_HTTP_PORTS` only. No non-empty `URLS`, no other `HTTP_PORTS` and no
   `Kestrel__Endpoints__*` (or `Kestrel:Endpoints:*`) setting, bare or with a `DOTNET_` or
   `ASPNETCORE_` prefix, is given to the API container (the host reads an empty `URLS` as unset,
   while an empty `HTTP_PORTS` override still replaces the port); no
   `--urls` argument is added to its command, and no app settings file setting the endpoint is
   published with it. The probe refuses, with exit 1 and the variable's name, any such setting it
   finds in its environment, compared without regard to case, rather than guessing which endpoint
   ASP.NET Core would choose. A command-line argument or an app settings file, which the probe
   cannot see, is excluded by this point alone.
4. **The HEALTHCHECK.** The Dockerfile publishes the probe to `/probe`, apart from the API's
   `/app`, and declares, in exec form:
   `HEALTHCHECK --interval=5s --timeout=5s --start-period=5s --retries=10 CMD ["dotnet", "/probe/SaasBtp.Api.HealthProbe.dll"]`.
   The endpoint and these options are those of the check Coolify ran with `curl` on staging:
   removing `curl` changes neither what gates the rolling update nor its timing (ADR-ARCH-024).
   The container is healthy only while the database answers, as it was under that check.
   The probe's 3-second limit leaves 2 seconds of the 5-second timeout for the runtime to start,
   so a slow answer is normally reported by the probe, with its cause; a run that still exceeds
   5 seconds is ended by Docker and counted as a failure all the same.
5. **The Coolify check.** The dashboard Healthcheck of every API application stays enabled, with
   type `cmd` and the command `dotnet /probe/SaasBtp.Api.HealthProbe.dll`, and with the interval,
   timeout, retries and start period of point 4 (5 s, 5 s, 10, 5 s), the values of the existing
   HTTP check: only the check's type and command change. The type `http` would run `curl` or
   `wget`, which the image no longer carries. The image's HEALTHCHECK is the check for every other runtime
   (`docker run`, Compose) and the reference that the dashboard command copies: a change to the
   probe's path or options changes both.
6. **The image checks.** `publish-api-images.yml` refuses to push an API image that declares no
   HEALTHCHECK, between its non-root check and the push by digest; `ci-api-images.yml` runs a copy
   of that check on every pull request. They check that a health check is declared, not that it
   passes: the probe's tests, which also read the Dockerfile's HEALTHCHECK, and the staging
   delivery prove that.
7. **What this supersedes in ADR-ARCH-022.** In §4, the words "with `curl` added for the Coolify
   health check". In its Consequences, the paragraph on `curl` ("`curl` adds a binary to the API
   image, ... only once its health check needs neither `curl` nor `wget`"), and, in "The
   Dockerfiles copy only their own project", the API Dockerfile, which copies the probe project as
   well. In its Alternatives considered, the `curl` reason given against SDK container publishing
   (its Dependabot reason stands), and the rejection of a chiseled image for the API for want of
   `curl` or `wget`, replaced by the reason given under Consequences below. Everything else in
   ADR-ARCH-022 stays in force. ADR-ARCH-022 stays accepted and unedited, as ADR-ARCH-035 did when
   ADR-ARCH-037 replaced parts of it.
8. **What this extends.** ADR-ARCH-030 §4's publication order becomes: build, check that the image
   runs as a non-root user, for the API check that it declares a HEALTHCHECK, push by digest, attest,
   tag. ADR-ARCH-024 §2.2 is unchanged: the rolling update stays gated, now by the `cmd` check.
   ADR-ARCH-020 places hosts under `hosts/`; the probe goes there as part of the API's runtime: it
   ships in the API image and runs inside the API container at run time, for as long as the host
   runs. The migration runner stays under `database/` for the reason ADR-ARCH-020 gives, a
   deployment tool that runs once before the application.

## Consequences
- The API image builds from the base images and NuGet alone: an outage of the Ubuntu archive no
  longer blocks a merge or a delivery.
- Coolify's check still runs through `/bin/sh` (a `CMD-SHELL` test). Moving the API to a
  chiseled image, which has no shell, would leave Coolify unable to run it, so this decision
  removes `curl` as an obstacle to a chiseled API image but not the shell; that move remains its
  own decision.
- A deployment of an image built before this decision, which has no `/probe`, fails the `cmd`
  check: its rolling update ends unhealthy, and Coolify keeps the running container. A manual
  rollback (ADR-ARCH-030) to such a digest needs the dashboard check switched to `http` first,
  and back to `cmd` right after the rollback has deployed, before any later deployment of a
  commit that carries this decision.
- The switch from `http` to `cmd` on staging is made once, immediately before the pull request
  that carries this decision is merged, when no staging delivery is running or queued, so that
  the first deployment under `cmd` is the first image with the probe.
- A variable listed in point 3, set in Coolify or in the API's Doppler config (ADR-ARCH-025),
  makes every deployment fail its health check, with the variable named in the health log, while
  the running container keeps serving.
- A wrong command or path in the dashboard check is caught by no workflow: only a staging
  deployment shows it, as a failed rolling update.
- The API Dockerfile copies two projects, each let back in by `.dockerignore`. The probe takes no
  project reference (point 2, checked by its tests); one added to the API needs the Dockerfile
  and `.dockerignore`, as ADR-ARCH-022 states.

## Alternatives considered
- `curl` or `wget` from another source (a copy from a builder stage, a static binary): rejected;
  it keeps a binary in the image only for the check, with its own updates, where the runtime is
  already present.
- Disabling the dashboard check so that Coolify uses the image's HEALTHCHECK: rejected; Coolify's
  source shows that a Docker Image application then has no gate at all.
- A probe mode in the API host itself (`dotnet SaasBtp.Api.dll --probe`): rejected by the
  maintainer for a separate project; the host would gain a command-line mode and every check would
  load the whole API.
- Making the probe follow every source of the endpoint as ASP.NET Core does: rejected; command-line
  arguments and app settings files cannot be read from the probe, and a partial copy of the
  precedence would report a healthy API as unhealthy, or the reverse, without saying why. One
  source, refused loudly otherwise, is checkable.
- Running the built image in CI until it is healthy: not decided here; issue #205 asks for the
  declaration check only.

## References
- Docker docs, Dockerfile reference, HEALTHCHECK (https://docs.docker.com/reference/dockerfile/#healthcheck):
  exit codes 0, 1 and 2; options and defaults; timeout; only the last HEALTHCHECK takes effect.
- Coolify docs (read 2026-11-11): Health checks
  (https://coolify.io/docs/applications/configuration/health-checks), Rolling updates
  (https://coolify.io/docs/applications/deployments/rolling-updates), Docker Image
  (https://coolify.io/docs/applications/deployments/docker-image).
- Coolify source, `coollabsio/coolify` at v4.4.6 (`f55efc8`) and v4.7.5 (`378ce37`):
  `app/Jobs/ApplicationDeploymentJob.php` (`rolling_update()`, `health_check()`,
  `generate_compose_file()`, `generate_healthcheck_commands()`), `app/Models/Application.php`
  (`isHealthcheckDisabled()`, `parseHealthcheckFromDockerfile()`).
- ASP.NET Core source, `dotnet/aspnetcore` branch `release/10.0`:
  `src/Hosting/Hosting/src/GenericHost/GenericWebHostService.cs` (`StartAsync`: an empty `urls`
  falls back to `HTTP_PORTS`; `ExpandPorts` splits with `TrimEntries` and `RemoveEmptyEntries`),
  `src/Servers/Kestrel/Core/src/Internal/AddressBinder.cs` (endpoints before addresses; the
  default address when neither is set).
- Microsoft Learn: "Configure endpoints for the ASP.NET Core Kestrel web server" (`HTTP_PORTS`),
  "WebApplication and WebApplicationBuilder" (`ASPNETCORE_URLS` overrides `ASPNETCORE_HTTP_PORTS`),
  "Configuration in ASP.NET Core" (default host configuration sources; host variables).
