"""Convert decoded BGR images to a bounded-memory Switch test asset pack."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct

from PIL import Image
from g3_image import GameImage
from g3_resources import parse_index, read_resource

MAGIC = b"G3NXRAW1"
HEADER = struct.Struct("<8sII")
RECORD = struct.Struct("<16sIIII")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", type=Path)
    parser.add_argument("--output", type=Path, default=Path("dist/switch/g3nx/g3nx.assets"))
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix(".partial")
    manifest = []
    try:
        with GameImage(args.image) as image, temporary.open("w+b") as out:
            records = parse_index(image.read("G3P1/BGRs/BGRs.idx"))
            out.write(HEADER.pack(MAGIC, len(records), 0))
            out.write(bytes(RECORD.size*len(records)))
            cache = {}
            directory = []
            for i, rec in enumerate(records):
                data = read_resource(image, "G3P1/BGRs/BGRs.idx", rec, cache)
                with Image.open(io.BytesIO(data)) as source:
                    original = source.size
                    fmt = source.format
                    # Preserve original assets; only this viewer copy is resized.
                    picture = source.convert("RGBA")
                    picture.thumbnail((1040, 624), Image.Resampling.LANCZOS)
                    raw = picture.tobytes()
                    w, h = picture.size
                offset = out.tell()
                if offset+len(raw) > 0xffffffff:
                    raise ValueError("Asset pack exceeds 32-bit offsets")
                out.write(raw)
                directory.append(RECORD.pack(rec.name.encode("ascii"), w, h, offset, len(raw)))
                manifest.append({"name": rec.name, "format": fmt, "original_size": original,
                    "viewer_size": [w, h], "offset": offset, "bytes": len(raw),
                    "source_sha256": hashlib.sha256(data).hexdigest()})
                if i == 1:
                    picture.save(args.output.parent/"preview.png")
            out.seek(HEADER.size)
            for rec in directory:
                out.write(rec)
        temporary.replace(args.output)
    except Exception:
        temporary.unlink(missing_ok=True)
        raise
    args.output.with_suffix(".json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"Converted {len(manifest)} images: {args.output} ({args.output.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
