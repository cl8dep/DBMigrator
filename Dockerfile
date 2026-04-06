# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Restore dependencies first (layer caching)
COPY DbMigrator.slnx ./
COPY src/DbMigrator/DbMigrator.csproj src/DbMigrator/
RUN dotnet restore src/DbMigrator/DbMigrator.csproj

COPY src/DbMigrator/ src/DbMigrator/
RUN dotnet publish src/DbMigrator/DbMigrator.csproj \
    -c Release \
    -o /out

# Stage 2: Runtime
# debian-slim is required (not scratch/distroless) because we need
# postgresql-client (pg_dump, pg_restore) in the final image.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

# Install postgresql-client for pg_dump / pg_restore
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        postgresql-client \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Run as non-root user for security
RUN groupadd --gid 1001 dbmigrator \
    && useradd --uid 1001 --gid dbmigrator --shell /bin/sh --create-home dbmigrator

WORKDIR /app
COPY --from=build /out ./
RUN chown -R dbmigrator:dbmigrator /app

USER dbmigrator

LABEL org.opencontainers.image.title="db-migrator" \
      org.opencontainers.image.description="PostgreSQL database migrator and sanitizer" \
      org.opencontainers.image.source="https://github.com/cl8dep/DBMigrator"

ENTRYPOINT ["dotnet", "db-migrator.dll"]
