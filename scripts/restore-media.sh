#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${MEDIA_BACKUP:?Set MEDIA_BACKUP}"; : "${MEDIA_ROOT:?Set MEDIA_ROOT}"
read -r -p "Type RESTORE to confirm extracting into an empty $MEDIA_ROOT: " ok; [[ "$ok" == RESTORE ]]
export MEDIA_BACKUP MEDIA_ROOT
python3 - <<'PY'
import os, tarfile
from pathlib import Path
root = Path(os.environ['MEDIA_ROOT']).resolve()
root.mkdir(parents=True, exist_ok=True)
if any(root.iterdir()):
    raise SystemExit('Restore target must be empty; archive the previous media directory first')
with tarfile.open(os.environ['MEDIA_BACKUP'], 'r:gz') as archive:
    members = archive.getmembers()
    for member in members:
        target = (root / member.name).resolve()
        if not target.is_relative_to(root) or not (member.isfile() or member.isdir()):
            raise SystemExit('Unsafe archive path or special/link member rejected')
    archive.extractall(root, members=members, filter='data')
print('Media restored. Verify ownership and file checksums before serving:', root)
PY
