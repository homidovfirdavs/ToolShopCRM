#!/bin/sh
# One-shot: role -> EF migrations (idempotent, additive) -> grants. Uses PG* env vars. Never drops anything.
set -eu
DIR="$(dirname "$0")"
for i in $(seq 1 30); do pg_isready -q && break; sleep 2; done
pg_isready -q || { echo "database not reachable"; exit 1; }
APP_USER="${APP_DB_USER:-toolshop_app}"
psql -v ON_ERROR_STOP=1 -q -f "$DIR/ensure-role.sql"
echo "applying EF migrations (idempotent script)"
psql -v ON_ERROR_STOP=1 -q -f "${MIGRATE_SQL:-/opt/migrate.sql}" >/dev/null
echo "applying grants"
psql -v ON_ERROR_STOP=1 -q -v app_user="$APP_USER" -v db="$PGDATABASE" -f "$DIR/grants.sql"
echo "migrate: done"
