#!/bin/sh
# Creates deploy/.env from .env.example and fills the empty secret fields with random values.
# Secrets are never printed. Refuses to overwrite an existing .env.
set -eu
cd "$(dirname "$0")"
OUT="${1:-.env}"
[ -e "$OUT" ] && { echo "$OUT already exists - not touching it."; exit 1; }
rnd() { LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c "$1"; }
umask 077
mkdir -p backups certs
while IFS= read -r line; do
  case "$line" in
    DB_OWNER_PASSWORD=)  echo "DB_OWNER_PASSWORD=$(rnd 32)" ;;
    APP_DB_PASSWORD=)    echo "APP_DB_PASSWORD=$(rnd 32)" ;;
    JWT_KEY=)            echo "JWT_KEY=$(rnd 64)" ;;
    ADMIN_PASSWORD=)     echo "ADMIN_PASSWORD=$(rnd 20)Aa1" ;;
    *) printf '%s\n' "$line" ;;
  esac
done < .env.example > "$OUT"
chmod 600 "$OUT"
echo "$OUT created (mode 600). Edit PUBLIC_ORIGIN/ports; read ADMIN_PASSWORD from the file, log in, then change it."
