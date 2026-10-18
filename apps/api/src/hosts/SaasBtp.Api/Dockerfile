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
RUN dotnet restore apps/api/src/hosts/SaasBtp.Api/SaasBtp.Api.csproj

COPY apps/api/src/hosts/SaasBtp.Api/ apps/api/src/hosts/SaasBtp.Api/

# The OpenAPI document is checked by CI (ADR-ARCH-018) and not used by the image: generating it
# would start the application during the image build.
RUN dotnet publish apps/api/src/hosts/SaasBtp.Api/SaasBtp.Api.csproj \
    -c Release --no-restore -o /app -p:OpenApiGenerateDocumentsOnBuild=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS final

# Coolify runs its HTTP health check inside the container with curl or wget; the base image
# ships neither.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# The base image creates the non-root user "app" and sets APP_UID to its id; it listens on
# ASPNETCORE_HTTP_PORTS, 8080.
EXPOSE 8080
ENTRYPOINT ["dotnet", "SaasBtp.Api.dll"]
