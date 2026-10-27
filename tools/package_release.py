"""Package only the runtime assets named in the verified current build report."""
import hashlib
import json
from pathlib import Path
import zipfile

ROOT=Path(__file__).resolve().parent.parent


def main():
    folder=ROOT/"dist/switch/g3nx"
    report=json.loads((folder/"build-report.json").read_text(encoding="utf-8"))
    runtime=report.get("runtime_files")
    if runtime is None:
        runtime={"g3nx.nro":report,"g3nx.title":report["assets"],"g3nx.story":report["story"]}
    required={"g3nx.nro","g3nx.title","g3nx.story"}
    if report["version"] in ("0.4.0","0.5.0"):
        required.update(("g3nx.field2","audio/manifest.json","audio/bgm0035.wav","audio/bgm0042.wav"))
    if report["version"]=="0.5.0":
        required.update(("g3nx.battle6","battle6.obt"))
    if not required.issubset(runtime):raise ValueError("Build report omits required runtime files")
    for name,expected in runtime.items():
        if not name or "\\" in name or Path(name).is_absolute() or ".." in Path(name).parts:
            raise ValueError("Unsafe runtime filename")
        with (folder/name).open("rb") as stream:
            digest=hashlib.file_digest(stream,"sha256").hexdigest()
        if digest!=expected["sha256"] or (folder/name).stat().st_size!=expected["bytes"]:
            raise ValueError(f"File changed since build verification: {name}")
    path=ROOT/"dist"/f"g3nx-scenario-{report['version']}.zip"
    with zipfile.ZipFile(path,"w",compression=zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for name in (*runtime,"README.txt","build-report.json"):
            archive.write(folder/name,"switch/g3nx/"+name)
    with zipfile.ZipFile(path) as archive:
        if archive.testzip() is not None:
            raise ValueError("ZIP checksum verification failed")
    print(f"Verified package: {path} ({path.stat().st_size:,} bytes)")


if __name__=="__main__":
    main()
