"""Build with official Linux or Windows devkitPro tools and record runtime hashes."""
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
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--toolchain",type=Path,default=Path(os.environ.get("DEVKITPRO",ROOT/".toolchain/local/opt/devkitpro")))
    args=parser.parse_args()
    folder=ROOT/"dist/switch/g3nx"
    required=[folder/n for n in ("g3nx.title","g3nx.story","g3nx.field2","g3nx.battle6","battle6.obt","audio/manifest.json")]
    required += [ROOT/"homebrew/include"/n for n in ("g3_story_data.h","g3_field2_data.h","g3_battle6_data.h")]
    for path in required:
        if not path.is_file():raise FileNotFoundError(f"Prepare all scenario/audio assets before building: {path}")
    tools=args.toolchain.resolve()
    suffix=".exe" if os.name=="nt" else ""
    cc=tools/f"devkitA64/bin/aarch64-none-elf-gcc{suffix}"
    nacptool=tools/f"tools/bin/nacptool{suffix}"
    elf2nro=tools/f"tools/bin/elf2nro{suffix}"
    for path in (cc,nacptool,elf2nro,tools/"libnx/lib/libnx.a"):
        if not path.is_file():raise FileNotFoundError(f"Missing toolchain component: {path}")
    source=ROOT/"homebrew"
    version=subprocess.check_output([str(cc),"--version"],text=True).splitlines()[0]
    env=os.environ.copy()
    env.update(DEVKITPRO=str(tools),DEVKITA64=str(tools/"devkitA64"))
    if os.name!="nt":
        subprocess.run(["make","-C",str(source),"-j2"],env=env,check=True)
    else:
        # Avoid MSYS mount/path translation on Windows-native installs.
        relative=Path(os.path.relpath(tools,source)).as_posix()
        env["DEVKITPRO"]=relative
        (source/"build").mkdir(exist_ok=True)
        def run(executable,*args):subprocess.run([str(executable),*args],cwd=source,env=env,check=True)
        arch=["-march=armv8-a+crc+crypto","-mtune=cortex-a57","-mtp=soft","-fPIE"]
        flags=arch+["-std=gnu11","-O2","-g","-Wall","-Wextra","-Werror",
                    "-ffunction-sections","-fdata-sections","-D__SWITCH__",
                    "-Iinclude","-I"+relative+"/libnx/include"]
        names=("main","g3_formats","g3_menu","g3_story","g3_field","g3_field_render","g3_progress","g3_audio","g3_map","g3_navigation","g3_combat","g3_combat_math")
        for name in names:run(cc,*flags,"-c",f"source/{name}.c","-o",f"build/{name}.o")
        run(cc,*[f"build/{name}.o" for name in names],*arch,
            "-specs="+relative+"/libnx/switch.specs","-Wl,--gc-sections,-Map,build/g3nx.map",
            "-L"+relative+"/libnx/lib","-lnx","-o","g3nx.elf")
        run(nacptool,"--create","G3NX - The War of Genesis III","G3NX","0.6.0","g3nx.nacp")
        run(elf2nro,"g3nx.elf","g3nx.nro","--nacp=g3nx.nacp")
    elf=(source/"g3nx.elf").read_bytes()
    if elf[:6]!=b"\x7fELF\x02\x01" or struct.unpack_from("<H",elf,18)[0]!=183:
        raise ValueError("ELF is not little-endian AArch64 ELF64")
    report=validate_nro(source/"g3nx.nro")
    report.update(compiler=version,version="0.6.0",hardware_tested=False,build_platform=os.name,
                  executable_fields=[1,2],scripted_actor_motion=True,bgm_tracks=[35,42],
                  free_movement_implemented=False,battle_runtime_implemented=False,
                  battle_navigation_workbench=True,original_ground_movement_cost=True,
                  manual_combat_workbench=True,physical_effect_kinds=[0],original_ai_implemented=False,
                  previous_user_hardware_check={"features":["music","character_movement"],"version":None},
                  native_checkpoint_implemented=True,voice_implemented=False)
    report["source_commit"]=subprocess.check_output(["git","rev-parse","HEAD"],cwd=ROOT,text=True).strip()
    report["source_dirty"]=bool(subprocess.check_output(["git","status","--porcelain"],cwd=ROOT,text=True).strip())
    shutil.copy2(source/"g3nx.nro",folder/"g3nx.nro")
    manifest=json.loads((folder/"audio/manifest.json").read_text())
    if {row["track"] for row in manifest}!={35,42}:raise ValueError("Audio manifest does not match this build")
    names=["g3nx.nro","g3nx.title","g3nx.story","g3nx.field2","g3nx.battle6","battle6.obt","audio/manifest.json"]
    for row in manifest:
        name=row["file"]
        if Path(name).name!=name or not name.endswith(".wav"):raise ValueError("Invalid audio filename")
        path=folder/"audio"/name
        with path.open("rb") as stream:digest=hashlib.file_digest(stream,"sha256").hexdigest()
        if digest!=row["sha256"]:raise ValueError("Audio changed since conversion: "+name)
        names.append("audio/"+name)
    report["runtime_files"]={}
    for name in names:
        path=folder/name
        with path.open("rb") as stream:digest=hashlib.file_digest(stream,"sha256").hexdigest()
        report["runtime_files"][name]=dict(bytes=path.stat().st_size,sha256=digest)
    (folder/"build-report.json").write_text(json.dumps(report,indent=2)+"\n")
    (folder/"README.txt").write_text(
        "G3NX 0.6.0 test build\n\n"
        "Copy the complete switch/g3nx folder to an SD card with an existing hbmenu setup.\n"
        "Start Game -> Shivan Scimitar. A advances dialogue; A at the first ending enters field 2.\n"
        "X saves dialogue or field-2 motion; Load Game resumes. Y toggles BGM mute. B returns. PLUS exits.\n"
        "Includes original field-2 scripted actor motion and BGM 35/42.\n"
        "A at the field-2 ending opens manual combat. Arrows select destination/target; A moves/attacks.\n"
        "Both teams are manual. R waits; L focuses current unit; X resets; ZL toggles old navigation.\n"
        "Includes period/team scheduling, kind-0 physical hits, critical damage, HP and KO. No combat saving.\n"
        "Original base map, units, elevation and ground costs; occupied cells are excluded.\n"
        "Static idle sprites only. No object-depth compositing, battle statuses/counters/skills, AI or BTL victory.\n"
        "No later story, voice/effects/movies. User verified music/movement in the previous version only.\n"
        "Palette fade and original frame timing fidelity remain unverified.\n"
        "NRO/ELF structure and host tests checked; Switch hardware execution untested.\n")
    print(json.dumps(report,indent=2))


if __name__=="__main__":
    main()
