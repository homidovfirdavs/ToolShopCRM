#!/bin/sh
# STAGING ONLY: self-signed certificate for local HTTPS tests. For production use a real certificate
# (Let's Encrypt / your CA) and place fullchain.pem + privkey.pem in deploy/certs/.
set -eu
cd "$(dirname "$0")"
CN="${1:-localhost}"
[ -e certs/privkey.pem ] && { echo "certs/privkey.pem exists - not overwriting."; exit 1; }
umask 077
openssl req -x509 -newkey rsa:2048 -nodes -days 90 -subj "/CN=$CN" \
  -addext "subjectAltName=DNS:$CN,DNS:localhost,IP:127.0.0.1" \
  -keyout certs/privkey.pem -out certs/fullchain.pem 2>/dev/null
chmod 644 certs/fullchain.pem
chmod 644 certs/privkey.pem   # container runs as non-root (uid 101); the folder itself is git-ignored
echo "Self-signed certificate for $CN written to deploy/certs/ (90 days, staging only)."
