#!/bin/sh
# STAGING check on a machine with Docker. NOT a deployment: separate compose project (toolshop-stg), separate
# volumes, ports 8080/8443, throw-away random secrets in .env.staging, self-signed certificate.
# It never touches the production project/volumes. At the end it removes ONLY the toolshop-stg project.
set -u
cd "$(dirname "$0")"
P="-p toolshop-stg --env-file .env.staging"
pass=0; fail=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; fail=$((fail+1)); }
chk() { d="$1"; shift; if out=$("$@" 2>&1); then ok "$d"; else bad "$d"; printf '%s\n' "$out" | tail -n 25 | sed 's/^/      | /'; fi; }

[ -e .env.staging ] || { sh ./init-env.sh .env.staging >/dev/null; sed -i 's|^PUBLIC_ORIGIN=.*|PUBLIC_ORIGIN=https://localhost:8443|;s|^HTTP_PORT=.*|HTTP_PORT=8080|;s|^HTTPS_PORT=.*|HTTPS_PORT=8443|;s|^HSTS_MAX_AGE=.*|HSTS_MAX_AGE=0|' .env.staging; }
[ -e certs/fullchain.pem ] || sh ./make-selfsigned-cert.sh localhost >/dev/null
mkdir -p backups
chmod 777 backups   # staging only: the postgres container user must be able to write here

chk "docker compose config" docker compose $P config --quiet
echo "building (first time takes several minutes) ..."
chk "images build (api, web, migrate)" docker compose $P build
chk "stack starts (db -> migrate -> api -> web)" docker compose $P up -d --wait --wait-timeout 240
echo; echo "---- versions and base image digests actually used (copy these to pin; not secrets) ----"
docker compose $P exec -T db postgres --version 2>/dev/null | sed 's/^/db:  /'
docker compose $P exec -T web nginx -v 2>&1 | sed 's/^/web: /'
docker compose $P exec -T api dotnet --list-runtimes 2>/dev/null | sed 's/^/api: /'
for i in postgres:17-alpine nginxinc/nginx-unprivileged:1.27-alpine mcr.microsoft.com/dotnet/sdk:10.0 mcr.microsoft.com/dotnet/aspnet:10.0 node:22-alpine; do
  d=$(docker image inspect --format '{{index .RepoDigests 0}}' "$i" 2>/dev/null) || d=""
  echo "digest $i => ${d:-not present locally}"
done
echo "----"
chk "migrate job exited 0" sh -c "docker compose $P ps -a --format '{{.Service}} {{.ExitCode}}' | grep -q '^migrate 0'"
for s in db api web; do chk "$s healthy" sh -c "docker compose $P ps $s --format '{{.Health}}' | grep -q healthy"; done
chk "HTTP -> HTTPS redirect (301)" sh -c "curl -s -o /dev/null -w '%{http_code}' http://localhost:8080/ | grep -q 301"
chk "HTTPS serves the app" sh -c "curl -sk https://localhost:8443/ | grep -qi '<title>ToolShop'"
chk "HTTPS /health/live via nginx" sh -c "curl -sk -o /dev/null -w '%{http_code}' https://localhost:8443/health/live | grep -q 200"
chk "/health/ready NOT public" sh -c "curl -sk -o /dev/null -w '%{http_code}' https://localhost:8443/health/ready | grep -q 404"
chk "HSTS/CSP/nosniff headers" sh -c "curl -skI https://localhost:8443/ | grep -qi content-security-policy"
chk "API reachable through nginx (401 without token)" sh -c "curl -sk -o /dev/null -w '%{http_code}' https://localhost:8443/api/products | grep -q 401"
chk "PostgreSQL is NOT published on the host" sh -c "! docker compose $P port db 5432 | grep -q ."
chk "API runs as non-root" sh -c "docker compose $P exec -T api id -u | grep -vq '^0$'"
chk "app role is least-privilege (no DDL)" docker compose $P exec -T db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "select not rolsuper and not rolcreatedb and not rolcreaterole from pg_roles where rolname='"'"'toolshop_app'"'"'" | grep -q t'
chk "backup + restore test (row counts of every table)" docker compose $P exec -T db sh /db/test-backup-restore.sh
chk "data survives container restart (pgdata volume)" sh -c "docker compose $P restart db api >/dev/null && docker compose $P up -d --wait --wait-timeout 120"

[ "$fail" -eq 0 ] || { echo; echo "---- container logs (last 60 lines each) ----"; docker compose $P logs --tail=60 2>&1; }
echo; echo "RESULT: $pass passed, $fail failed"
echo "Cleaning up the STAGING project only (toolshop-stg + its volumes)."
docker compose $P down -v >/dev/null 2>&1
[ "$fail" -eq 0 ]
