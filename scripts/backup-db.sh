#!/usr/bin/env bash
# Backup of the "schoollibrary" PostgreSQL database. Runs on the server.
# Uses the local socket inside the postgres container, so no password is needed.
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-$HOME/backups/schoollibrary}"
KEEP_DAYS="${KEEP_DAYS:-14}"

mkdir -p "$BACKUP_DIR"
file="$BACKUP_DIR/schoollibrary-$(date +%F-%H%M).sql.gz"

docker exec postgres pg_dump -U schoollib -d schoollibrary | gzip > "$file"

# Delete backups older than KEEP_DAYS
find "$BACKUP_DIR" -name 'schoollibrary-*.sql.gz' -mtime +"$KEEP_DAYS" -delete

echo "Backup written to $file"
