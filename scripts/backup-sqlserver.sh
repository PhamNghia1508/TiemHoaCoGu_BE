#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${BACKUP_FILE:?Set BACKUP_FILE to a NEW path on the SQL Server host}"
: "${SQLSERVER_DATABASE:?Set SQLSERVER_DATABASE}"; : "${SQLSERVER_SERVER:?Set SQLSERVER_SERVER}"
: "${SQLSERVER_USER:?Set SQLSERVER_USER}"; : "${SQLSERVER_PASSWORD:?Set password externally}"
[[ "$SQLSERVER_DATABASE" =~ ^[A-Za-z_][A-Za-z0-9_]{0,127}$ ]] || { echo 'Use a simple ASCII database identifier.' >&2; exit 1; }
file=${BACKUP_FILE//\'/\'\'}
args=(-b -S "$SQLSERVER_SERVER" -U "$SQLSERVER_USER")
if [[ "${SQLSERVER_TRUST_CERTIFICATE:-no}" == yes ]]; then args+=(-C); fi
# The destination is on the SQL Server host, not necessarily this shell host.
SQLCMDPASSWORD="$SQLSERVER_PASSWORD" sqlcmd "${args[@]}" -Q "DECLARE @exists INT; EXEC master.dbo.xp_fileexist N'$file', @exists OUTPUT; IF @exists=1 THROW 51000, 'Backup destination already exists; choose a new path.', 1; BACKUP DATABASE [$SQLSERVER_DATABASE] TO DISK=N'$file' WITH COPY_ONLY, CHECKSUM, COMPRESSION;"
