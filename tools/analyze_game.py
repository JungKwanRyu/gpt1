"""Reproduce static analysis. Original binaries are never executed."""
import argparse
from collections import Counter
from dataclasses import asdict
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent/"reverse/vendor"))
from g3_image import GameImage
from g3_resources import parse_index, parse_strings, read_resource, xor_text


def dump_pe(path, output):
    import pefile
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    pe = pefile.PE(str(path))
    base = pe.OPTIONAL_HEADER.ImageBase
    imports = {e.dll.decode(): [i.name.decode() if i.name else f"ordinal:{i.ordinal}" for i in e.imports]
               for e in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])}
    iat = {i.address: (e.dll.decode()+"!"+(i.name.decode() if i.name else str(i.ordinal)))
           for e in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []) for i in e.imports}
    exports = [{"name": e.name.decode() if e.name else None, "va": base+e.address, "ordinal": e.ordinal}
               for e in getattr(getattr(pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])]
    strings = {}
    for sec in pe.sections:
        if sec.Characteristics & 0x20000000:
            continue
        for m in re.finditer(rb"[ -~]{4,}\x00", sec.get_data()):
            strings[base+sec.VirtualAddress+m.start()] = m.group()[:-1].decode()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.skipdata = True
    references = []
    with (output/(path.name+".asm")).open("w", encoding="utf-8") as out:
        for sec in pe.sections:
            if not sec.Characteristics & 0x20000000:
                continue
            for ins in md.disasm(sec.get_data(), base+sec.VirtualAddress):
                comments = []
                for token in re.findall(r"0x[0-9a-f]+", ins.op_str):
                    value = int(token, 16)
                    if value in strings:
                        comments.append(repr(strings[value]))
                        if re.search(r"idx|pak|chr|fld|cpr|episode|\.dlg|\.clg|\.elg|\.eps|\.dat", strings[value], re.I):
                            references.append({"va": ins.address, "text_va": value, "text": strings[value]})
                    if value in iat:
                        comments.append(iat[value])
                out.write(f"{ins.address:08x}: {ins.mnemonic:8} {ins.op_str}"+(" ; "+", ".join(comments) if comments else "")+"\n")
    return {"sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "machine": pe.FILE_HEADER.Machine,
            "image_base": base, "entry": base+pe.OPTIONAL_HEADER.AddressOfEntryPoint,
            "imports": imports, "exports": exports, "resource_string_references": references}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", type=Path)
    parser.add_argument("--output", type=Path, default=Path("reverse"))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    report = {"image": str(args.image), "indices": {}, "texts": {}, "characters": [], "pe": {}}
    with GameImage(args.image) as image:
        report["file_count"] = sum(not e.directory for e in image.entries)
        report["extensions"] = dict(Counter(PurePosixPath(e.path).suffix.lower() for e in image.entries if not e.directory))
        for filename in ("G3P1/G3Launch.exe", "G3P1/alpha.dll"):
            path = image.extract(filename, args.output/"original")
            report["pe"][path.name] = dump_pe(path, args.output)
        for e in image.entries:
            if e.path.lower().endswith(".idx"):
                cache = {}
                records = parse_index(image.read(e.path))
                matched = compared = 0
                for rec in records:
                    data = read_resource(image, e.path, rec, cache)
                    loose = str(PurePosixPath(e.path).parent/rec.name)
                    if loose.casefold() in image.by_path:
                        compared += 1
                        matched += image.read(loose) == data
                report["indices"][e.path] = {"count": len(records), "loose_compared": compared,
                    "loose_identical": matched, "records": [asdict(r) for r in records]}
                print(f"{e.path}: {len(records)} valid records; loose identical {matched}/{compared}")
                del cache
            elif PurePosixPath(e.path).suffix.lower() in (".dlg", ".clg", ".flg", ".elg"):
                try:
                    report["texts"][e.path] = {"strings": parse_strings(image.read(e.path))}
                except (ValueError, UnicodeDecodeError) as exc:
                    report["texts"][e.path] = {"error": str(exc)}
            elif e.path.lower().endswith(".chr"):
                data = image.read(e.path)
                if len(data) >= 58:
                    try:
                        report["characters"].append({"path": e.path, "size": len(data),
                            "name": xor_text(data[:24]), "english_name": xor_text(data[24:48]),
                            "short_name": xor_text(data[48:58])})
                    except UnicodeDecodeError as exc:
                        report["characters"].append({"path": e.path, "error": str(exc)})
    (args.output/"analysis.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    good = sum("strings" in v for v in report["texts"].values())
    print(f"Text tables decoded: {good}/{len(report['texts'])}; characters: {len(report['characters'])}")


if __name__ == "__main__":
    main()
