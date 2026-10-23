#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${MEDIA_ROOT:?Set MEDIA_ROOT}"; : "${BACKUP_FILE:?Set BACKUP_FILE}"
mkdir -p "$(dirname "$BACKUP_FILE")"; tar --xattrs --acls -czf "$BACKUP_FILE" -C "$MEDIA_ROOT" .
