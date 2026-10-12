#!/bin/sh
# Full logical backup (pg_dump custom format) + readability check + SHA-256. Runs inside a postgres container
# (or anywhere with PG* env set). Read-only for the database.
set -eu
OUT="${BACKUP_DIR:-/backups}"
DB="${PGDATABASE:-${POSTGRES_DB:?}}"
[ -w "$OUT" ] || { echo "backup dir $OUT is not writable (Linux host: chown it to the postgres uid, see docs)"; exit 1; }
f="$OUT/$DB-$(date +%Y%m%d-%H%M%S).dump"
if ! pg_dump -d "$DB" -Fc -Z 6 --no-owner --no-privileges -f "$f.part"; then rm -f "$f.part"; echo "pg_dump failed"; exit 1; fi
if ! pg_restore --list "$f.part" >/dev/null; then rm -f "$f.part"; echo "backup unreadable"; exit 1; fi
mv "$f.part" "$f"
( cd "$OUT" && sha256sum "$(basename "$f")" > "$(basename "$f").sha256" )
find "$OUT" -maxdepth 1 -name "$DB-*.dump*" -mtime +"${BACKUP_KEEP_DAYS:-14}" -type f -delete
echo "$f"
