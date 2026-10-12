# syntax=docker/dockerfile:1
# ToolShop CRM API. Build context = the backend/ folder.
#   target "runtime" (default, last) : the API image
#   target "migrate"                 : one-shot DB migration job (idempotent EF script + psql)
# No secrets live in this file or in the image: everything comes from environment variables at run time.

# Base images are pinned by digest (verified in CI 2026-10-12: PostgreSQL 17.11, .NET 10.0.12). To update: run the
# "Docker staging check" workflow, copy the printed digests here, re-run it.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
COPY ToolShop.Domain/ToolShop.Domain.csproj ToolShop.Domain/
COPY ToolShop.Infrastructure/ToolShop.Infrastructure.csproj ToolShop.Infrastructure/
COPY ToolShop.API/ToolShop.API.csproj ToolShop.API/
RUN dotnet restore ToolShop.API/ToolShop.API.csproj
COPY ToolShop.Domain/ ToolShop.Domain/
COPY ToolShop.Infrastructure/ ToolShop.Infrastructure/
COPY ToolShop.API/ ToolShop.API/
RUN dotnet publish ToolShop.API/ToolShop.API.csproj -c Release -o /out/app --no-restore /p:UseAppHost=false

# ---- idempotent SQL for all EF migrations (generated, never applied here) ----
FROM build AS migrations
RUN dotnet tool install --global dotnet-ef --version 10.0.0
ENV PATH="$PATH:/root/.dotnet/tools"
# Build-time placeholders only, so the host can be created for design-time. They are NOT used at run time and NOT secrets.
ENV ConnectionStrings__DefaultConnection="Host=build-only;Database=build-only;Username=build-only" \
    Jwt__Key="build-time-placeholder-not-a-secret-0123456789" \
    Jwt__Issuer="build" Jwt__Audience="build"
RUN dotnet ef migrations script --idempotent \
      --project ToolShop.Infrastructure --startup-project ToolShop.API \
      --output /out/migrate.sql

# ---- one-shot migration job: runs /db/migrate.sh (mounted by docker-compose) ----
FROM postgres:17-alpine@sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24 AS migrate
COPY --from=migrations /out/migrate.sql /opt/migrate.sql

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS runtime
# curl is only used by the container HEALTHCHECK
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app
WORKDIR /app
COPY --from=build /out/app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_gcServer=0 \
    DOTNET_EnableDiagnostics=0
USER app
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
  CMD curl -fsS http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "ToolShop.API.dll"]
