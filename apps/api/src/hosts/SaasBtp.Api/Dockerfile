# The API image (ADR-ARCH-022). Build context: the repository root, filtered by the root
# .dockerignore. Built for linux/amd64:
#   docker build --platform linux/amd64 -f apps/api/src/hosts/SaasBtp.Api/Dockerfile .
#
# Base images are pinned by digest; the tag next to each digest is informative only. A digest
# bump that moves the SDK to another feature band moves the global.json pin in the same pull
# request, or the build stage refuses global.json.

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /src

# The files that decide the SDK, the build settings and the package versions come first, so
# that the restore layer is reused while only source code changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY apps/api/src/hosts/SaasBtp.Api/SaasBtp.Api.csproj apps/api/src/hosts/SaasBtp.Api/
COPY apps/api/src/hosts/SaasBtp.Api.HealthProbe/SaasBtp.Api.HealthProbe.csproj apps/api/src/hosts/SaasBtp.Api.HealthProbe/
RUN dotnet restore apps/api/src/hosts/SaasBtp.Api/SaasBtp.Api.csproj \
    && dotnet restore apps/api/src/hosts/SaasBtp.Api.HealthProbe/SaasBtp.Api.HealthProbe.csproj

COPY apps/api/src/hosts/SaasBtp.Api/ apps/api/src/hosts/SaasBtp.Api/
COPY apps/api/src/hosts/SaasBtp.Api.HealthProbe/ apps/api/src/hosts/SaasBtp.Api.HealthProbe/

# The OpenAPI document is checked by CI (ADR-ARCH-018) and not used by the image: generating it
# would start the application during the image build.
RUN dotnet publish apps/api/src/hosts/SaasBtp.Api/SaasBtp.Api.csproj \
    -c Release --no-restore -o /app -p:OpenApiGenerateDocumentsOnBuild=false

# The readiness probe of the HEALTHCHECK below, published apart from the API so that its files
# never mix with the host's (ADR-ARCH-039).
RUN dotnet publish apps/api/src/hosts/SaasBtp.Api.HealthProbe/SaasBtp.Api.HealthProbe.csproj \
    -c Release --no-restore -o /probe

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS final

# Nothing is installed in this stage: the image holds the base image, the API and the probe,
# and its build downloads nothing beyond the base images and NuGet (ADR-ARCH-039).
WORKDIR /app
COPY --from=build /app .
COPY --from=build /probe /probe

# The base image creates the non-root user "app" and sets APP_UID to its id; it listens on
# ASPNETCORE_HTTP_PORTS, 8080.
USER $APP_UID
EXPOSE 8080

# The health check runs the probe on the .NET runtime the image already holds: GET
# /health/ready, which runs the database readiness check, on 127.0.0.1 at the first port of
# ASPNETCORE_HTTP_PORTS, exit 0 on a 2xx and 1 otherwise (ADR-ARCH-039). It keeps the endpoint
# and the timing of the check Coolify ran with curl on staging. The probe gives up after 3 s,
# inside the 5 s timeout here.
# SaasBtp.Api.HealthProbe.Tests reads this line: keep it on one line. Coolify does not read it
# for a Docker Image application; its dashboard check runs the same command (ADR-ARCH-039).
HEALTHCHECK --interval=5s --timeout=5s --start-period=5s --retries=10 CMD ["dotnet", "/probe/SaasBtp.Api.HealthProbe.dll"]
ENTRYPOINT ["dotnet", "SaasBtp.Api.dll"]
