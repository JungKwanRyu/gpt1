#!/usr/bin/env python3
"""Prepare only the portable game inputs from the pinned public PC release.

Usage: python prepare_game_files.py [--package existing.nupkg] [--destination release-directory]
The default destination is beside this script under switch/WogSwitchProbe/release.
Existing game files are never overwritten.
"""
import argparse
import hashlib
from pathlib import Path, PurePosixPath
import shutil
import tempfile
import urllib.request
import zipfile

URL = 'https://github.com/Kyeongrok/the-war-of-genesis-clicker/releases/download/v1.0.86/WarOfGenesis-1.0.86-full.nupkg'
SHA256 = 'fa8b26aca5d0f6efe79813c653401e236d85af68d98c04129e2e5e303fbac848'
GAME_DLLS = {'WarOfGenesis.dll', 'WarOfGenesis.Assets.dll'}


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def prepare(package, destination):
    if digest(package) != SHA256:
        raise ValueError('PC package SHA-256 mismatch; expected the official v1.0.86 full package')
    if destination.exists() and any(p.name != 'README.txt' for p in destination.iterdir()):
        raise FileExistsError('Destination contains existing files. Choose an empty release folder: ' + str(destination))
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='wog-inputs-', dir=destination.parent) as temp:
        staging = Path(temp)
        with zipfile.ZipFile(package) as archive:
            selected = []
            for info in archive.infolist():
                if info.is_dir() or '\\' in info.filename:
                    continue
                path = PurePosixPath(info.filename)
                if len(path.parts) < 3 or path.parts[:2] != ('lib', 'app') or '..' in path.parts:
                    continue
                relative = PurePosixPath(*path.parts[2:])
                if str(relative) in GAME_DLLS or relative.parts[0] == 'assets' or str(relative) == 'sq.version':
                    selected.append((info, relative))
            if not GAME_DLLS.issubset({str(p) for _, p in selected}):
                raise ValueError('The official package is missing required game DLLs')
            for info, relative in selected:
                file = staging.joinpath(*relative.parts)
                file.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(info) as source, file.open('wb') as target:
                    shutil.copyfileobj(source, target)
        destination.mkdir(exist_ok=True)
        for file in staging.rglob('*'):
            if file.is_file():
                target = destination / file.relative_to(staging)
                target.parent.mkdir(parents=True, exist_ok=True)
                with file.open('rb') as source, target.open('xb') as output:
                    shutil.copyfileobj(source, output)
    print('Prepared v1.0.86 game inputs:', len(selected), 'files in', destination)
    print('Windows runtime DLLs and executables were excluded. Original game resources are separate.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path)
    parser.add_argument('--destination', type=Path, default=Path(__file__).resolve().parent / 'switch/WogSwitchProbe/release')
    args = parser.parse_args()
    if args.package:
        prepare(args.package, args.destination)
    else:
        with tempfile.TemporaryDirectory(prefix='wog-package-') as temp:
            package = Path(temp) / 'game.nupkg'
            print('Downloading the official PC package (about 92 MB)...', flush=True)
            with urllib.request.urlopen(URL, timeout=180) as source, package.open('wb') as target:
                shutil.copyfileobj(source, target)
            prepare(package, args.destination)


if __name__ == '__main__':
    main()
