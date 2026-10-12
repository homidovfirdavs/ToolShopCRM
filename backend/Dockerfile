# syntax=docker/dockerfile:1
# ToolShop CRM API. Build context = the backend/ folder.
#   target "runtime" (default, last) : the API image
#   target "migrate"                 : one-shot DB migration job (idempotent EF script + psql)
# No secrets live in this file or in the image: everything comes from environment variables at run time.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
FROM postgres:17-alpine AS migrate
COPY --from=migrations /out/migrate.sql /opt/migrate.sql

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
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
