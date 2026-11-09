#!/usr/bin/env python3
"""Run the shipped host in an isolated PC tool folder and move its SD cache."""
import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess
import tempfile

FILES = ["WogSwitch.Managed.dll", "WogPatcher.dll", "WogShim.dll", "Mono.Cecil.dll", "SkiaSharp.dll",
         "WogSwitch.Managed.runtimeconfig.json", "WogSwitch.Managed.deps.json"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="wog-pc-prep-") as temporary:
        root = Path(temporary); runtime = root / "pc-runtime"; runtime.mkdir()
        for name in FILES: shutil.copy2(args.host / name, runtime / name)
        first = root / "first-card/switch/WogSwitchProbe"
        shutil.copytree(args.fixture, first / "release")
        (first / "managed").mkdir()
        for name in FILES[:5]: shutil.copy2(runtime / name, first / "managed" / name)
        (first / "original/part2/TXR").mkdir(parents=True)
        (first / "original/part2/Chr").mkdir(); (first / "original/part2/Obs").mkdir()
        (first / "original/part2/TXR/Txr.dat").write_bytes(b"SYNTHETIC ORIGINAL DATA")
        hashes = {name: hashlib.sha256((first / "release" / name).read_bytes()).digest()
                  for name in ["WarOfGenesis.dll", "WarOfGenesis.Assets.dll"]}
        def prepare(card, name):
            result = subprocess.run(["dotnet", str(runtime / "WogSwitch.Managed.dll"), "--prepare-sd", str(card)], text=True, encoding="utf-8", errors="replace", capture_output=True)
            (args.output / name).write_text(result.stdout + result.stderr)
            if result.returncode: raise RuntimeError(result.stdout + result.stderr)
            assert "PC PREPARE COMPLETE" in result.stdout and "Main invoked: no" in result.stdout
            return result.stdout
        assert "Cache: built" in prepare(first, "pc-prepare-cold.log")
        assert (first / "save/assets/data/TXR/Txr.dat").is_file()
        assert (first / "asset-report.txt").is_file()
        second = root / "moved-card/switch/WogSwitchProbe"
        shutil.copytree(first, second)
        assert "Cache: verified hit" in prepare(second, "pc-prepare-moved.log")
        assert all(hashlib.sha256((second / "release" / name).read_bytes()).digest() == value for name, value in hashes.items())
        print("PASS actual shipped host runs from an isolated PC folder without native Skia, prepatches DLLs, extracts synthetic original data and reuses its cache after SD-root relocation")


if __name__ == "__main__": main()
