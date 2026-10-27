"""Read-only FAT16 access to the supplied game image. No mounts or OS changes."""
from __future__ import annotations

import argparse
from dataclasses import dataclass, asdict
import json
from pathlib import Path, PurePosixPath
import struct


@dataclass(frozen=True)
class Entry:
    path: str
    cluster: int
    size: int
    directory: bool


class GameImage:
    def __init__(self, path):
        self.file = open(path, "rb")
        try:
            mbr = self._at(0, 512)
            if mbr[510:512] != b"\x55\xaa":
                raise ValueError("Missing MBR signature")
            partitions = [mbr[i:i+16] for i in range(446, 510, 16)]
            part = next((p for p in partitions if p[4] in (4, 6, 14)), None)
            if part is None:
                raise ValueError("No FAT16 partition")
            self.base = struct.unpack_from("<I", part, 8)[0] * 512
            b = self._at(self.base, 512)
            self.sector = struct.unpack_from("<H", b, 11)[0]
            self.spc = b[13]
            reserved, = struct.unpack_from("<H", b, 14)
            fats = b[16]
            roots, = struct.unpack_from("<H", b, 17)
            fat_sectors, = struct.unpack_from("<H", b, 22)
            if self.sector not in (512, 1024, 2048, 4096) or not self.spc or self.spc & (self.spc-1):
                raise ValueError("Invalid FAT geometry")
            self.cluster_size = self.sector * self.spc
            self.fat = self._at(self.base + reserved * self.sector, fat_sectors*self.sector)
            self.root_at = self.base + (reserved+fats*fat_sectors)*self.sector
            self.root_size = roots*32
            self.data_at = self.root_at + ((self.root_size+self.sector-1)//self.sector)*self.sector
            self.entries = list(self._walk("", self._at(self.root_at, self.root_size), set()))
            self.by_path = {e.path.casefold(): e for e in self.entries}
        except Exception:
            self.file.close()
            raise

    def close(self):
        self.file.close()

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()

    def _at(self, offset, size):
        self.file.seek(offset)
        data = self.file.read(size)
        if len(data) != size:
            raise ValueError(f"Truncated image at {offset:#x}")
        return data

    def _chain(self, cluster, limit=None):
        seen = set()
        chunks = []
        count = 0
        while cluster < 0xfff8:
            if cluster < 2 or cluster*2+2 > len(self.fat) or cluster in seen:
                raise ValueError("Invalid or cyclic FAT chain")
            seen.add(cluster)
            take = self.cluster_size if limit is None else min(self.cluster_size, limit-count)
            chunks.append(self._at(self.data_at+(cluster-2)*self.cluster_size, take))
            count += take
            if limit is not None and count >= limit:
                break
            cluster, = struct.unpack_from("<H", self.fat, cluster*2)
        if limit is not None and count < limit:
            raise ValueError("File is shorter than directory size")
        return b"".join(chunks)

    def _walk(self, parent, data, ancestors):
        long_name = {}
        for pos in range(0, len(data), 32):
            d = data[pos:pos+32]
            if d[0] == 0:
                break
            if d[0] == 0xe5:
                long_name.clear()
                continue
            if d[11] == 15:
                long_name[d[0] & 31] = d[1:11]+d[14:26]+d[28:32]
                continue
            if d[11] & 8:
                long_name.clear()
                continue
            short = d[:8].rstrip().decode("cp949", "replace")
            ext = d[8:11].rstrip().decode("cp949", "replace")
            name = short + ("."+ext if ext else "")
            if long_name:
                name = b"".join(long_name[k] for k in sorted(long_name)).decode("utf-16le").split("\0", 1)[0].rstrip("\uffff")
            long_name.clear()
            if name in (".", ".."):
                continue
            if "/" in name or "\\" in name:
                raise ValueError("Invalid directory name")
            cluster, = struct.unpack_from("<H", d, 26)
            size, = struct.unpack_from("<I", d, 28)
            path = parent+"/"+name if parent else name
            entry = Entry(path, cluster, size, bool(d[11] & 16))
            yield entry
            if entry.directory:
                if cluster in ancestors:
                    raise ValueError("Cyclic directory")
                yield from self._walk(path, self._chain(cluster), ancestors | {cluster})

    def read(self, path, limit=None):
        e = self.by_path[path.replace("\\", "/").casefold()]
        if e.directory:
            raise ValueError("Cannot read a directory as a file")
        size = e.size if limit is None else min(e.size, limit)
        return self._chain(e.cluster, size) if size else b""

    def extract(self, path, destination):
        # Reject untrusted path traversal and existing symlink components.
        e = self.by_path[path.replace("\\", "/").casefold()]
        parts = PurePosixPath(e.path).parts
        if e.directory or any(p in ("..", ".") or ":" in p for p in parts):
            raise ValueError("Invalid extraction path")
        root = Path(destination).resolve()
        target = root.joinpath(*parts)
        if not target.resolve().is_relative_to(root):
            raise ValueError("Extraction escapes destination")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(self.read(e.path))
        return target


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", type=Path)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--extract", nargs="*")
    parser.add_argument("--output", type=Path, default=Path("reverse/original"))
    args = parser.parse_args()
    with GameImage(args.image) as image:
        if args.manifest:
            args.manifest.parent.mkdir(parents=True, exist_ok=True)
            args.manifest.write_text(json.dumps([asdict(e) for e in image.entries], indent=2, ensure_ascii=False), encoding="utf-8")
        for path in args.extract or []:
            print(image.extract(path, args.output))
        print(f"{sum(not e.directory for e in image.entries)} files, {sum(e.directory for e in image.entries)} directories")


if __name__ == "__main__":
    main()
