#!/usr/bin/env python3
"""Build a native Mono interpreter diagnostic NRO, without a host .NET SDK.

Requires an unmodified NroForge checkout and its verified bundled SDK.
This does not build or run WarOfGenesis, and does not rewrite its assemblies.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def run(args, **kwargs):
    subprocess.run([str(a) for a in args], check=True, **kwargs)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--nroforge', required=True, type=Path)
    parser.add_argument('--devkitpro', required=True, type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'build/switch-probe')
    parser.add_argument('--managed', type=Path, help='Overlay published managed checkpoint DLLs')
    parser.add_argument('--skia', type=Path, help='Verified native SkiaSharp archive/resolver directory for checkpoint 0.3')
    parser.add_argument('--checkpoint-version', choices=['0.4.0', '0.5.0', '0.5.1', '0.5.2'], help='Build the game patch/load or startup checkpoint')
    args = parser.parse_args()
    version = '0.3.0' if args.skia else ('0.2.0' if args.managed else '0.1.0')
    if args.checkpoint_version:
        if not args.skia or not args.managed:
            parser.error('This checkpoint requires both managed and native Skia artifacts')
        version = args.checkpoint_version
    skia_info = None
    if args.skia:
        if not args.managed:
            parser.error('--skia requires --managed')
        skia_info = json.loads((args.skia / 'build-info.json').read_text())
        archive = args.skia / 'libSkiaSharp.a'
        if hashlib.sha256(archive.read_bytes()).hexdigest() != skia_info['archive_sha256']:
            parser.error('Native SkiaSharp archive hash mismatch')
        if not (args.skia / 'skia_exports.c').is_file():
            parser.error('Native SkiaSharp resolver missing')
        if not (args.managed / 'assets/NanumGothic.ttf').is_file():
            parser.error('Bundled Korean font missing')
    if args.managed:
        for name in ['WogSwitch.Managed.dll', 'SkiaSharp.dll', 'Mono.Cecil.dll', 'WogShim.dll', 'WogPatcher.dll']:
            if not (args.managed / name).is_file():
                parser.error('Missing managed dependency: ' + name)
    forge, devkit, output = args.nroforge.resolve(), args.devkitpro.resolve(), args.output.resolve()
    if output.exists():
        parser.error('Output must be a new directory, to preserve prior build evidence')
    sys.path.insert(0, str(forge))
    from scripts.sdk import ensure
    from nroforge import check_nro
    sdk = ensure(forge, forge / '.nroforge/sdk')
    mono = sdk / 'sdk/mono'
    icu = sdk / 'sdk/icu'
    shared = forge / 'runner/shared'
    bcl = mono / 'artifacts/bin/runtime/net9.0-libnx-Debug-arm64'
    mono_lib = mono / 'artifacts/obj/mono/libnx.arm64.Debug/out/lib'
    native_lib = mono / 'artifacts/bin/native/net9.0-libnx-Debug-arm64'
    output.mkdir(parents=True)
    objects = output / 'objects'
    objects.mkdir()
    env = dict(os.environ)
    env['DEVKITPRO'] = str(devkit)
    env['DEVKITA64'] = str(devkit / 'devkitA64')
    env['PATH'] = f'{devkit}/devkitA64/bin:{devkit}/tools/bin:' + env.get('PATH', '')
    cc = devkit / 'devkitA64/bin/aarch64-none-elf-gcc'
    # GCC 16 rejects legacy dlshim function declarations by default. The SDK
    # headers were written for GCC 15; use its gnu17 language mode explicitly.
    arch = ['-march=armv8-a+crc+crypto', '-mtune=cortex-a57', '-mtp=soft', '-fPIE']
    flags = [*arch, '-std=gnu17', '-O2', '-g', '-Wall', '-ffunction-sections', '-fdata-sections',
             '-D__SWITCH__', '-DU_DISABLE_RENAMING=1',
             '-I' + str(shared), '-I' + str(icu / 'include'),
             '-I' + str(devkit / 'libnx/include'),
             '-I' + str(mono / 'artifacts/bin/mono/libnx.arm64.Debug/include/mono-2.0')]
    if args.managed:
        flags += ['-I' + str(sdk / 'sdk/mesa/include'), '-I' + str(devkit / 'portlibs/switch/include')]
    if args.skia:
        flags += ['-DWOG_HAS_SKIA=1']
    if version in ['0.4.0', '0.5.0', '0.5.1', '0.5.2']:
        flags += ['-DWOG_GAME_LOADER=1', '-DWOG_CHECKPOINT_VERSION="' + version + '"']
    sources = [ROOT / ('switch/runtime/main.c' if args.managed else 'switch/probe/main.c')] + [shared / f for f in [
        'core.c', 'heap.c', 'io_util.c', 'io_shims.c', 'dl_shim.c',
        'dl_shim_dotnet.c', 'dl_shim_libnx.c', 'third_party/ini/ini.c']]
    if args.skia:
        sources += [args.skia / 'skia_exports.c']
    object_files = []
    for i, source in enumerate(sources):
        obj = objects / f'{i}-{source.stem}.o'
        run([cc, *flags, '-c', source, '-o', obj], env=env)
        object_files.append(obj)
    libs = [native_lib / f'lib{name}.a' for name in [
        'System.IO.Compression.Native', 'System.Globalization.Native', 'System.Native']]
    libs += [icu / 'lib' / f'lib{name}.a' for name in ['icui18n', 'icuuc', 'icudata']]
    libs += [mono_lib / f'lib{name}.a' for name in [
        'monosgen-2.0', 'mono-component-debugger-stub-static',
        'mono-component-diagnostics_tracing-stub-static',
        'mono-component-hot_reload-stub-static', 'mono-component-marshal-ilgen-static']]
    libs += [mono / 'artifacts/obj/mono/libnx.arm64.Debug/_deps/fetchzlibng-build/libz.a']
    if args.managed:
        mesa = sdk / 'sdk/mesa/lib'
        portlibs = devkit / 'portlibs/switch/lib'
        libs += [mesa / n for n in ['libEGL.a', 'libglapi.a', 'libvulkan.a']]
        libs += sorted((mesa / 'rust').glob('*.rlib'))
        libs += [portlibs / n for n in ['libdrm_nouveau.a', 'libexpat.a', 'libzstd.a']]
    if args.skia:
        libs += [args.skia / 'libSkiaSharp.a']
    elf = output / 'WogSwitchProbe.elf'
    run([cc, *arch, '-specs=' + str(devkit / 'libnx/switch.specs'),
         '-Wl,-Map,' + str(output / 'WogSwitchProbe.map'), *object_files,
         '-Wl,--start-group', *libs, '-L' + str(devkit / 'libnx/lib'),
         '-lnx', '-lm', '-lstdc++', '-pthread', '-Wl,--end-group', '-o', elf], env=env)
    header = elf.read_bytes()[:64]
    if header[:6] != b'\x7fELF\x02\x01' or struct.unpack_from('<H', header, 18)[0] != 183:
        raise ValueError('Expected little-endian AArch64 ELF64')
    app = output / 'sd/switch/WogSwitchProbe'
    app.mkdir(parents=True)
    managed = app / 'managed'
    managed.mkdir()
    for file in bcl.glob('*.dll'):
        shutil.copy2(file, managed / file.name)
    shutil.copy2(mono / 'artifacts/bin/mono/libnx.arm64.Debug/System.Private.CoreLib.dll', managed)
    for name in ['WogShim.dll', 'WogPatcher.dll']:
        shutil.copy2(ROOT / 'WogiOS_project/libs' / name, managed)
    if args.managed:
        for file in args.managed.glob('*.dll'):
            if file.name.startswith(('System.', 'Microsoft.')) or file.name in ['mscorlib.dll', 'netstandard.dll']:
                raise ValueError('Do not replace the matching Switch BCL with desktop assemblies: ' + file.name)
            shutil.copy2(file, managed / file.name)
    if args.skia:
        shutil.copytree(args.managed / 'assets', app / 'assets')
        shutil.copytree(args.skia / 'licenses', app / 'licenses/native-skia')
        shutil.copy2(args.skia / 'build-info.json', app / 'native-skia-build-info.json')
    if version in ['0.4.0', '0.5.0', '0.5.1', '0.5.2']:
        shutil.copy2(ROOT / 'switch/launcher.ini', app / ('launcher.ini' if version == '0.4.0' else 'launcher.ini.example'))
    shutil.copy2(sdk / 'runtime/mono/etc/icudt77l.dat', app)
    shutil.copy2(ROOT / 'switch/probe/probe.ini', app)
    romfs = output / 'romfs'
    romfs.mkdir()
    (romfs / 'probe.txt').write_text('WogSwitchProbe ' + version + ': compatibility checkpoint only.\n')
    nacp = output / 'WogSwitchProbe.nacp'
    run(['nacptool', '--create', 'WogSwitch Runtime Probe', 'twgios port experiment', version, nacp], env=env)
    nro = app / 'WogSwitchProbe.nro'
    run(['elf2nro', elf, nro, '--nacp=' + str(nacp), '--romfsdir=' + str(romfs)], env=env)
    check_nro(nro)
    manifests = []
    for file in sorted((output / 'sd').rglob('*')):
        if file.is_file():
            manifests.append({'file': str(file.relative_to(output / 'sd')),
                              'bytes': file.stat().st_size,
                              'sha256': hashlib.sha256(file.read_bytes()).hexdigest()})
    report = {'purpose': ('Switch platform patches and optional real game startup; no gameplay verified' if version in ['0.5.0', '0.5.1', '0.5.2'] else
                         'Real game DLL patch/load validation; Main is not invoked' if version == '0.4.0' else
                         'Native SkiaSharp image/Korean rendering checkpoint; not a game launcher' if args.skia else
                         'Managed Wog methods and EGL/input checkpoint; not a game launcher' if args.managed
                          else 'Mono interpreter and .NET 10 Wog DLL metadata probe; not a game launcher'),
              'version': version,
              'game_execution_supported': False,
              'optional_game_main_invocation': version in ['0.5.0', '0.5.1', '0.5.2'],
              'hardware_tested': False, 'managed_execution_tested': False,
              'compiler': subprocess.check_output([cc, '--version'], text=True).splitlines()[0],
              'nroforge_commit': subprocess.check_output(['git', '-C', forge, 'rev-parse', 'HEAD'], text=True).strip(),
              'toolchain_archive_sha256': json.loads((forge / 'toolchain.lock.json').read_text())['sha256'],
              'runtime_framework': 'net9.0-libnx', 'game_libraries_framework': 'net10.0',
              'native_skia': skia_info,
              'missing_game_dependencies': (['game platform integration', 'audio/save integration'] if args.skia else
                                             ['Switch native SkiaSharp', 'game platform integration'] if args.managed
                                             else ['SkiaSharp 3.119.0.0', 'Mono.Cecil 0.11.6.0']),
              'format_checks': {'elf64_aarch64': True, 'nro_and_nacp_romfs': True},
              'files': manifests}
    (output / 'build-report.json').write_text(json.dumps(report, indent=2) + '\n')
    print('Built diagnostic only:', nro)


if __name__ == '__main__':
    main()
