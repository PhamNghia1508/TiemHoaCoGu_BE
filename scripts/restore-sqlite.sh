#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${SQLITE_BACKUP:?Set SQLITE_BACKUP}"; : "${SQLITE_DB:?Set SQLITE_DB}"
: "${CONFIRM_APP_STOPPED:?Stop every API/worker instance and set CONFIRM_APP_STOPPED=yes}"
[[ "$CONFIRM_APP_STOPPED" == yes ]] || { echo 'Application must be stopped.' >&2; exit 1; }
read -r -p "Type RESTORE to confirm replacing $SQLITE_DB: " ok; [[ "$ok" == RESTORE ]]
export SQLITE_BACKUP SQLITE_DB
python3 - <<'PY'
import os, sqlite3, tempfile
from pathlib import Path
from urllib.parse import quote
source = Path(os.environ['SQLITE_BACKUP']).resolve(strict=True)
target = Path(os.environ['SQLITE_DB']).resolve()
if source == target:
    raise SystemExit('Source and destination must differ')
with sqlite3.connect('file:' + quote(str(source)) + '?mode=ro', uri=True) as backup:
    if backup.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
        raise SystemExit('Backup integrity check failed')
target.parent.mkdir(parents=True, exist_ok=True)
if target.exists():
    raise SystemExit('Move the existing stopped database and its -wal/-shm files aside first; restore never overwrites an existing database')
if Path(str(target) + '-wal').exists() or Path(str(target) + '-shm').exists():
    raise SystemExit('Remove or archive stale WAL/SHM files from the stopped database first')
handle, temporary = tempfile.mkstemp(prefix='.sqlite-restore-', dir=target.parent)
os.close(handle)
try:
    with sqlite3.connect('file:' + quote(str(source)) + '?mode=ro', uri=True) as src:
        with sqlite3.connect(temporary) as dst:
            src.backup(dst)
    with open(temporary, 'rb') as restored:
        os.fsync(restored.fileno())
    os.replace(temporary, target)
finally:
    Path(temporary).unlink(missing_ok=True)
print('SQLite restored. Run schema upgrade/readiness checks before resuming traffic:', target)
PY
