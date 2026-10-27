"""Reconstruct G3 resource indices and XOR-encoded text from measured bytes."""
from __future__ import annotations
from dataclasses import asdict, dataclass
from pathlib import PurePosixPath
import struct


@dataclass(frozen=True)
class Resource:
    name: str
    archive: str
    start: int
    end: int  # Inclusive, verified against the next entry and loose copies.

    @property
    def size(self):
        return self.end-self.start+1


def local_name(raw):
    raw = raw.split(b"\0", 1)[0]
    name = raw.decode("ascii")
    if not name or name in (".", "..") or any(c in name for c in "/\\:"):
        raise ValueError("Invalid resource name")
    return name


def parse_index(data):
    if len(data) < 4:
        raise ValueError("Truncated IDX header")
    count, = struct.unpack_from("<I", data)
    if len(data) != 4+count*30:
        raise ValueError("IDX size disagrees with record count")
    result = []
    for i in range(count):
        rec = data[4+30*i:34+30*i]
        name, archive = local_name(rec[:9]), local_name(rec[9:22])
        start, end = struct.unpack_from("<II", rec, 22)
        if end < start:
            raise ValueError("Invalid IDX range")
        # Snd.idx contains duplicate names in different archives. Preserve order;
        # lookup precedence belongs to the original loader, not this parser.
        result.append(Resource(name, archive, start, end))
    return result


def xor_text(raw):
    return bytes(b ^ 255 for b in raw.split(b"\xff", 1)[0]).decode("cp949", errors="strict")


def parse_strings(data):
    if len(data) < 4:
        raise ValueError("Truncated text header")
    count, payload_size = struct.unpack_from("<HH", data)
    base = 4+count*2
    if base+payload_size != len(data):
        raise ValueError("Text length disagrees with header")
    offsets = struct.unpack_from("<"+"H"*count, data, 4)
    result = []
    for off in offsets:
        if off >= payload_size or b"\xff" not in data[base+off:]:
            raise ValueError("Invalid text offset or missing terminator")
        result.append(xor_text(data[base+off:]))
    return result


def read_resource(image, index_path, resource, cache):
    folder = PurePosixPath(index_path).parent
    path = str(folder/resource.archive)
    if path not in cache:
        cache.clear()  # Bounded to one archive, including the large sound bank.
        cache[path] = image.read(path)
    data = cache[path]
    if resource.end >= len(data):
        raise ValueError(f"Out-of-bounds resource: {path}:{resource.name}")
    return data[resource.start:resource.end+1]
