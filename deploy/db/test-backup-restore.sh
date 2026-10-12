#!/bin/sh
# Proves a backup is restorable: backup -> restore into <db>_restore_test -> compare row count of EVERY table
# -> drop only that disposable database. Exit 0 = PASS. Stop writes (or accept differences) while it runs.
set -eu
DIR="$(dirname "$0")"
SRC="${PGDATABASE:-${POSTGRES_DB:?}}"; DST="${SRC}_restore_test"
counts() {
  q=$(psql -d "$1" -Atc "SELECT string_agg(format('SELECT %L AS t, count(*) AS n FROM %I.%I', tablename, schemaname, tablename), ' UNION ALL ' ORDER BY tablename) FROM pg_tables WHERE schemaname='public'")
  [ -n "$q" ] || { echo ""; return; }
  psql -d "$1" -At -F'|' -c "SELECT t, n FROM ($q) x ORDER BY t"
}
file=$(sh "$DIR/backup.sh" | tail -n 1)
RECREATE=yes sh "$DIR/restore.sh" "$file" "$DST"
a=$(counts "$SRC"); b=$(counts "$DST")
status=0
[ -n "$a" ] || { echo "FAIL: source has no tables"; status=1; }
if [ "$a" != "$b" ]; then echo "FAIL: row counts differ"; echo "--- source"; echo "$a"; echo "--- restored"; echo "$b"; status=1; fi
if [ $status -eq 0 ]; then
  echo "table|rows"; echo "$a"
  echo "PASS: $(echo "$a" | wc -l | tr -d ' ') tables - restored copy matches the source. backup: $file"
fi
psql -d postgres -qc "DROP DATABASE IF EXISTS \"$DST\" WITH (FORCE)"
echo "disposable database $DST removed"
exit $status
