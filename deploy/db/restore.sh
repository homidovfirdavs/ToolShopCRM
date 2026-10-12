#!/bin/sh
# Restore a backup into a NEW database. It never overwrites or drops an existing database, except a disposable
# one (name ends with _test) when RECREATE=yes. To recover production: restore to a new name, check it, then
# point DB_NAME at it (see docs). usage:  restore.sh <backup.dump> <new_database_name>
set -eu
FILE="${1:?backup file}"; TARGET="${2:?target database}"
DIR="$(dirname "$0")"
case "$TARGET" in *[!a-z0-9_]*|"") echo "target name: lowercase letters, digits, _ only"; exit 1;; esac
[ -f "$FILE" ] || { echo "file not found: $FILE"; exit 1; }
if [ -f "$FILE.sha256" ]; then
  ( cd "$(dirname "$FILE")" && sha256sum -c "$(basename "$FILE").sha256" >/dev/null ) || { echo "SHA-256 mismatch - not restoring"; exit 1; }
  echo "SHA-256 ok"
else echo "no .sha256 next to the backup - integrity not verified"; fi
exists=$(psql -d postgres -Atc "SELECT 1 FROM pg_database WHERE datname='$TARGET'")
if [ -n "$exists" ]; then
  case "$TARGET" in
    *_test) [ "${RECREATE:-}" = yes ] || { echo "$TARGET exists; set RECREATE=yes (disposable *_test only)"; exit 1; }
            psql -d postgres -qc "DROP DATABASE \"$TARGET\" WITH (FORCE)";;
    *) echo "database $TARGET already exists - refusing (choose a new name)"; exit 1;;
  esac
fi
psql -d postgres -qc "CREATE DATABASE \"$TARGET\""
pg_restore -d "$TARGET" --no-owner --no-privileges --exit-on-error "$FILE" || { echo "pg_restore failed - do not use $TARGET"; exit 1; }
psql -v ON_ERROR_STOP=1 -q -d "$TARGET" -v app_user="${APP_DB_USER:-toolshop_app}" -v db="$TARGET" -f "$DIR/grants.sql"
echo "restored: $TARGET"
