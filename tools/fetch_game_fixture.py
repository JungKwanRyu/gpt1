#!/usr/bin/env python3
"""Fetch the pinned public PC mod release for a build-runner compatibility audit.

The downloaded game is not included in our port's distributable artifacts.
"""
import hashlib
import json
from pathlib import Path
import urllib.request
import zipfile

URL = 'https://github.com/Kyeongrok/the-war-of-genesis-clicker/releases/download/v1.0.86/WarOfGenesis-1.0.86-full.nupkg'
SHA256 = 'fa8b26aca5d0f6efe79813c653401e236d85af68d98c04129e2e5e303fbac848'
target = Path('work/game-fixture')
target.mkdir(parents=True, exist_ok=True)
package = target / 'game.nupkg'
with urllib.request.urlopen(URL, timeout=180) as response, package.open('wb') as stream:
    while block := response.read(1024 * 1024):
        stream.write(block)
assert hashlib.sha256(package.read_bytes()).hexdigest() == SHA256
release = target / 'release'
release.mkdir(exist_ok=True)
with zipfile.ZipFile(package) as archive:
    assert archive.testzip() is None
    names = archive.namelist()
    print('PACKAGE ENTRIES', json.dumps(names[:100]))
    for name in names:
        parts = Path(name).parts
        if len(parts) < 3 or parts[0] != 'lib' or '..' in parts:
            continue
        relative = Path(*parts[2:])
        if name.endswith('/'):
            continue
        destination = release / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(archive.read(name))
print('Verified public game fixture:', SHA256)
