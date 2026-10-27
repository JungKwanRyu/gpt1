"""Package only the runtime assets named in the verified current build report."""
import hashlib
import json
from pathlib import Path
import zipfile

ROOT=Path(__file__).resolve().parent.parent


def main():
    folder=ROOT/"dist/switch/g3nx"
    report=json.loads((folder/"build-report.json").read_text(encoding="utf-8"))
    for name,expected in (("g3nx.nro",report),("g3nx.title",report["assets"]),("g3nx.story",report["story"])):
        with (folder/name).open("rb") as stream:
            digest=hashlib.file_digest(stream,"sha256").hexdigest()
        if digest!=expected["sha256"]:
            raise ValueError(f"File changed since build verification: {name}")
    path=ROOT/"dist"/f"g3nx-scenario-{report['version']}.zip"
    with zipfile.ZipFile(path,"w",compression=zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for name in ("g3nx.nro","g3nx.title","g3nx.story","README.txt","build-report.json"):
            archive.write(folder/name,"switch/g3nx/"+name)
    with zipfile.ZipFile(path) as archive:
        if archive.testzip() is not None:
            raise ValueError("ZIP checksum verification failed")
    print(f"Verified package: {path} ({path.stat().st_size:,} bytes)")


if __name__=="__main__":
    main()
