# The migration runner image (ADR-ARCH-022). Build context: the repository root, filtered by
# the root .dockerignore. Built for linux/amd64:
#   docker build --platform linux/amd64 -f apps/api/database/SaasBtp.Migrations/Dockerfile .
#
# Base images are pinned by digest; the tag next to each digest is informative only. A digest
# bump that moves the SDK to another feature band moves the global.json pin in the same pull
# request, or the build stage refuses global.json.

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /src

# The files that decide the SDK, the build settings and the package versions come first, so
# that the restore layer is reused while only source code or scripts change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY apps/api/database/SaasBtp.Migrations/SaasBtp.Migrations.csproj apps/api/database/SaasBtp.Migrations/
RUN dotnet restore apps/api/database/SaasBtp.Migrations/SaasBtp.Migrations.csproj

COPY apps/api/database/SaasBtp.Migrations/ apps/api/database/SaasBtp.Migrations/
RUN dotnet publish apps/api/database/SaasBtp.Migrations/SaasBtp.Migrations.csproj \
    -c Release --no-restore -o /app

# Chiseled: no shell and no package manager. The runner needs neither: it reads its connection
# string from ConnectionStrings__Migrations and exits.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:75e4232c875a07e46e447ca0a56a61f7cd72d9fb361639018a19e9fc605c6827 AS final
WORKDIR /app
COPY --from=build /app .

# The base image already runs as the non-root user APP_UID; stated again so the image does not
# depend on that default.
USER $APP_UID
USER root
ENTRYPOINT ["dotnet", "SaasBtp.Migrations.dll"]
