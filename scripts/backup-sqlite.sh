#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${SQLITE_DB:?Set SQLITE_DB}"; : "${BACKUP_FILE:?Set BACKUP_FILE}"
export SQLITE_DB BACKUP_FILE
python3 - <<'PY'
import os, sqlite3
from pathlib import Path
from urllib.parse import quote
source = Path(os.environ['SQLITE_DB']).resolve(strict=True)
target = Path(os.environ['BACKUP_FILE']).resolve()
if source == target:
    raise SystemExit('Source and backup must differ')
target.parent.mkdir(parents=True, exist_ok=True)
with target.open('xb'):
    pass
try:
    with sqlite3.connect('file:' + quote(str(source)) + '?mode=ro', uri=True) as src:
        with sqlite3.connect(str(target)) as dst:
            src.backup(dst)
            if dst.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
                raise RuntimeError('Backup integrity check failed')
    with target.open('rb') as saved:
        os.fsync(saved.fileno())
except Exception:
    target.unlink(missing_ok=True)
    raise
print('SQLite backup created and integrity checked:', target)
PY
