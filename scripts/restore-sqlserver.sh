#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${SQLSERVER_BACKUP:?Set backup path on the SQL Server host}"; : "${SQLSERVER_DATABASE:?Set SQLSERVER_DATABASE}"
: "${SQLSERVER_SERVER:?Set SQLSERVER_SERVER}"; : "${SQLSERVER_USER:?Set SQLSERVER_USER}"; : "${SQLSERVER_PASSWORD:?Set secret externally}"
: "${CONFIRM_APP_STOPPED:?Stop every API/worker instance and set CONFIRM_APP_STOPPED=yes}"
[[ "$CONFIRM_APP_STOPPED" == yes ]] || { echo 'Application must be stopped.' >&2; exit 1; }
[[ "$SQLSERVER_DATABASE" =~ ^[A-Za-z_][A-Za-z0-9_]{0,127}$ ]] || { echo 'Use a simple ASCII database identifier.' >&2; exit 1; }
read -r -p "Type RESTORE to confirm REPLACING $SQLSERVER_DATABASE from $SQLSERVER_BACKUP: " ok; [[ "$ok" == RESTORE ]]
file=${SQLSERVER_BACKUP//\'/\'\'}
args=(-b -S "$SQLSERVER_SERVER" -U "$SQLSERVER_USER")
if [[ "${SQLSERVER_TRUST_CERTIFICATE:-no}" == yes ]]; then args+=(-C); fi
SQLCMDPASSWORD="$SQLSERVER_PASSWORD" sqlcmd "${args[@]}" -Q "RESTORE VERIFYONLY FROM DISK=N'$file' WITH CHECKSUM; RESTORE DATABASE [$SQLSERVER_DATABASE] FROM DISK=N'$file' WITH REPLACE, RECOVERY;"
