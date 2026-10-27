"""Build the libnx viewer with Windows-native devkitPro tools, without MSYS mounts."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess

ROOT = Path(__file__).resolve().parent.parent


def validate_nro(path):
    data = path.read_bytes()
    if data[16:20] != b"NRO0":
        raise ValueError("Missing NRO0 header")
    size, = struct.unpack_from("<I", data, 24)
    if size < 128 or size+56 > len(data):
        raise ValueError("Invalid NRO size")
    segments = []
    previous_end = 0
    for name, at in (("text",32),("rodata",40),("data",48)):
        offset, length = struct.unpack_from("<II",data,at)
        if offset < previous_end or offset+length > size or offset % 4096 or length % 4096:
            raise ValueError(f"Invalid {name} segment")
        segments.append({"name":name,"offset":offset,"size":length})
        previous_end = offset+length
    if data[size:size+4] != b"ASET":
        raise ValueError("Missing NRO asset header")
    version, = struct.unpack_from("<I",data,size+4)
    if version != 0:
        raise ValueError("Unknown asset header version")
    nacp_offset, nacp_size = struct.unpack_from("<QQ",data,size+24)
    if nacp_offset < 56 or nacp_size != 16384 or size+nacp_offset+nacp_size > len(data):
        raise ValueError("Invalid NACP range")
    nacp = data[size+nacp_offset:size+nacp_offset+nacp_size]
    title = nacp[:512].split(b"\0",1)[0].decode("utf-8")
    if title != "G3NX - The War of Genesis III":
        raise ValueError(f"Unexpected application title: {title}")
    return {"bytes":len(data),"sha256":hashlib.sha256(data).hexdigest(),
            "title":title,"nro_size":size,"segments":segments,"nacp_size":nacp_size}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--toolchain",type=Path,default=ROOT/".toolchain/local/opt/devkitpro")
    args = parser.parse_args()
    for required in (ROOT/"dist/switch/g3nx/g3nx.title",ROOT/"dist/switch/g3nx/g3nx.story",
                     ROOT/"homebrew/include/g3_story_data.h"):
        if not required.is_file():
            raise FileNotFoundError(f"Prepare title/story assets before building: {required}")
    source = ROOT/"homebrew"
    tools = args.toolchain.resolve()
    cc = tools/"devkitA64/bin/aarch64-none-elf-gcc.exe"
    nacptool = tools/"tools/bin/nacptool.exe"
    elf2nro = tools/"tools/bin/elf2nro.exe"
    for path in (cc,nacptool,elf2nro,tools/"libnx/lib/libnx.a"):
        if not path.is_file():
            raise FileNotFoundError(f"Missing toolchain component: {path}")
    relative = Path(os.path.relpath(tools,source)).as_posix()
    env = os.environ.copy()
    env["DEVKITPRO"] = relative
    (source/"build").mkdir(exist_ok=True)

    def run(executable,*arguments):
        subprocess.run([str(executable),*arguments],cwd=source,env=env,check=True)

    arch = ["-march=armv8-a+crc+crypto","-mtune=cortex-a57","-mtp=soft","-fPIE"]
    flags = arch+["-std=gnu11","-O2","-g","-Wall","-Wextra","-Werror",
                  "-ffunction-sections","-fdata-sections","-D__SWITCH__","-Iinclude",
                  "-I"+relative+"/libnx/include"]
    version = subprocess.check_output([str(cc),"--version"],text=True).splitlines()[0]
    for name in ("main","g3_formats","g3_menu","g3_story"):
        run(cc,*flags,"-c",f"source/{name}.c","-o",f"build/{name}.o")
    run(cc,"build/main.o","build/g3_formats.o","build/g3_menu.o","build/g3_story.o",*arch,
        "-specs="+relative+"/libnx/switch.specs","-Wl,--gc-sections,-Map,build/g3nx.map",
        "-L"+relative+"/libnx/lib","-lnx","-o","g3nx.elf")
    elf = (source/"g3nx.elf").read_bytes()
    if elf[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<H",elf,18)[0] != 183:
        raise ValueError("ELF is not little-endian AArch64 ELF64")
    run(nacptool,"--create","G3NX - The War of Genesis III","G3NX","0.3.0","g3nx.nacp")
    run(elf2nro,"g3nx.elf","g3nx.nro","--nacp=g3nx.nacp")
    report = validate_nro(source/"g3nx.nro")
    report.update({"compiler":version,"version":"0.3.0","hardware_tested":False,
                   "title_screen_implemented":True,"episode_selection_implemented":True,
                   "opening_scenario_implemented":"Shivan field 1; partial visual presentation; no audio",
                   "native_checkpoint_implemented":True,"gameplay_implemented":False,
                   "prior_0_2_0_hardware_confirmed_by_user":True})
    destination = ROOT/"dist/switch/g3nx"
    destination.mkdir(parents=True,exist_ok=True)
    shutil.copy2(source/"g3nx.nro",destination/"g3nx.nro")
    assets = destination/"g3nx.title"
    if assets.exists():
        with assets.open("rb") as stream:
            report["assets"] = {"file":assets.name,"bytes":assets.stat().st_size,
                                "sha256":hashlib.file_digest(stream,"sha256").hexdigest()}
    story_asset=destination/"g3nx.story"
    if not story_asset.is_file():
        raise FileNotFoundError("Run tools/prepare_story.py before packaging")
    with story_asset.open("rb") as stream:
        report["story"]={"file":story_asset.name,"bytes":story_asset.stat().st_size,
                         "sha256":hashlib.file_digest(stream,"sha256").hexdigest()}
    (destination/"build-report.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    (destination/"README.txt").write_text(
        "G3NX - The War of Genesis III 0.3.0\n\n"
        "Copy the switch folder to the SD card root.\n"
        "Required: switch/g3nx/g3nx.nro, g3nx.title, g3nx.story\n"
        "Launch G3NX - The War of Genesis III from your existing hbmenu.\n"
        "D-pad / left stick: select. A: confirm. B: back. PLUS: exit.\n\n"
        "Select Shivan Scimitar to run its original opening dialogue script.\n"
        "A: next dialogue page. X: save native progress. B: episode selection.\n"
        "Load Game restores G3NX checkpoints; original PC saves are not supported.\n"
        "Other episodes, field movement, battles, tutorial and audio are not implemented.\n"
        "NRO format/build checked; Nintendo Switch hardware execution not yet tested.\n",
        encoding="utf-8")
    print(json.dumps(report,indent=2))
    print(f"Packaged: {destination/'g3nx.nro'}")


if __name__ == "__main__":
    main()
