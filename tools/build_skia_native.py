#!/usr/bin/env python3
"""Build the real SkiaSharp 3.119 C API for devkitA64/newlib.

Run on the Linux GitHub build runner, inside the pinned devkitPro container.
No managed API replacements or Linux/Android binaries are used on Switch.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

SKIA_COMMIT = '30abf4a0dcd3006f3a555e46906151e486f87b6e'
SKIA_URL = 'https://github.com/mono/skia.git'
DEPENDENCIES = ['expat', 'freetype', 'libjpeg-turbo', 'libpng', 'libwebp', 'wuffs', 'zlib']


def run(args, **kwargs):
    subprocess.run(list(map(str, args)), check=True, **kwargs)


def fetch(repo, url, commit):
    if not (repo / '.git').is_dir():
        repo.mkdir(parents=True, exist_ok=True)
        run(['git', 'init', repo])
        run(['git', '-C', repo, 'remote', 'add', 'origin', url])
    current = subprocess.run(['git', '-C', repo, 'rev-parse', 'HEAD'], capture_output=True, text=True)
    if current.returncode or current.stdout.strip() != commit:
        run(['git', '-C', repo, '-c', 'http.version=HTTP/1.1', 'fetch', '--depth', '1', 'origin', commit])
        run(['git', '-C', repo, 'checkout', '--detach', 'FETCH_HEAD'])
    assert subprocess.check_output(['git', '-C', repo, 'rev-parse', 'HEAD'], text=True).strip() == commit


def patch_platform(skia):
    # Horizon has file I/O but no POSIX mmap interface. Skia explicitly supports
    # mmap failure and then reads the file into owned memory. Use that real
    # fallback for fonts/images; never reinterpret a Linux mmap implementation.
    p = skia / 'src/ports/SkOSFile_posix.cpp'
    data = p.read_text()
    if 'WOG_SWITCH_NO_FILE_MMAP' not in data:
        data = data.replace('#include <sys/mman.h>', '#if !defined(__SWITCH__)\n#include <sys/mman.h>\n#endif')
        a = data.index('void sk_fmunmap(')
        b = data.index('\nint sk_fileno(', a)
        original = data[a:b]
        data = data[:a] + '''// WOG_SWITCH_NO_FILE_MMAP: use SkData's owned-file-buffer fallback.
#if defined(__SWITCH__)
void sk_fmunmap(const void*, size_t) { }
void* sk_fdmmap(int, size_t*) { return nullptr; }
#else
''' + original + '\n#endif\n' + data[b:]
        p.write_text(data)
    # The upstream CPU-only C wrapper omitted its null return argument here.
    # Preserve its documented unavailable-GPU behavior without enabling Ganesh.
    p = skia / 'src/c/gr_context.cpp'
    data = p.read_text()
    old = 'return SK_ONLY_GPU(ToGrDirectContext(GrAsDirectContext(AsGrRecordingContext(context))));'
    new = 'return SK_ONLY_GPU(ToGrDirectContext(GrAsDirectContext(AsGrRecordingContext(context))), nullptr);'
    if old not in data and new not in data:
        raise ValueError('Upstream direct-context wrapper changed')
    p.write_text(data.replace(old, new))
    # Only optional GPU test tools use SkOSLibrary's dlopen API. They are not
    # built for this static CPU library; do not compile a nonexistent OS loader.
    p = skia / 'BUILD.gn'
    data = p.read_text()
    line = '      "src/ports/SkOSLibrary_posix.cpp",'
    replacement = '      # WOG_SWITCH_STATIC_ONLY: no POSIX dynamic loader on Horizon.'
    if line not in data and replacement not in data:
        raise ValueError('Upstream OS loader source list changed')
    p.write_text(data.replace(line, replacement))
    # Chromium zlib's ARM dispatcher reads Linux auxv/hwcap headers. Use its
    # supported portable implementation on Horizon, rather than claiming Linux.
    p = skia / 'third_party/zlib/BUILD.gn'
    data = p.read_text()
    old = '  use_arm_neon_optimizations =\n      (current_cpu == "arm" || current_cpu == "arm64") && !(is_win && !is_clang)'
    new = '  use_arm_neon_optimizations = false  # WOG_SWITCH_PORTABLE_ZLIB'
    if old not in data and new not in data:
        raise ValueError('Upstream zlib dispatcher configuration changed')
    p.write_text(data.replace(old, new))
    # Match the existing SK_ONLY_GPU convention for newer GPU factories which
    # upstream forgot to guard in CPU-only builds. Raster implementations stay
    # intact; GPU creation correctly reports unavailable (null).
    factories = {
        'src/c/sk_surface.cpp': ['sk_surface_new_backend_render_target',
            'sk_surface_new_backend_texture', 'sk_surface_new_render_target'],
        'src/c/sk_image.cpp': ['sk_image_new_from_texture',
            'sk_image_new_from_adopted_texture', 'sk_image_make_texture_image'],
    }
    for filename, functions in factories.items():
        p = skia / filename
        data = p.read_text()
        for name in functions:
            pattern = r'(' + name + r'\([^\n]*\) \{\n    return )([^\n]+)(;\n\})'
            found = re.search(pattern, data)
            if not found:
                raise ValueError('Upstream GPU factory changed: ' + name)
            if not found[2].startswith('SK_ONLY_GPU('):
                data = data[:found.start(2)] + 'SK_ONLY_GPU(' + found[2] + ', nullptr)' + data[found.end(2):]
        p.write_text(data)
    p = skia / 'src/c/sk_image.cpp'
    data = p.read_text()
    name = 'sk_image_make_with_filter'
    pattern = r'(' + name + r'\([^\n]*\) \{\n)(    return [^\n]+;)(\n\})'
    found = re.search(pattern, data)
    if found:
        body = '#if defined(SK_GANESH)\n' + found[2] + '\n#else\n    return sk_image_make_with_filter_raster(cimage, filter, subset, clipBounds, outSubset, outOffset);\n#endif'
        data = data[:found.start(2)] + body + data[found.end(2):]
        p.write_text(data)
    elif 'return sk_image_make_with_filter_raster(cimage, filter' not in data:
        raise ValueError('Upstream filtered-image factory changed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', type=Path, default=Path('work/skia'))
    parser.add_argument('--output', type=Path, default=Path('output/skia-native'))
    parser.add_argument('--devkitpro', type=Path, default=Path('/opt/devkitpro'))
    parser.add_argument('--jobs', type=int, default=2)
    args = parser.parse_args()
    skia, output, dk = args.work.resolve(), args.output.resolve(), args.devkitpro.resolve()
    fetch(skia, SKIA_URL, SKIA_COMMIT)
    deps = {}
    for name in DEPENDENCIES:
        match = re.search(r'"third_party/externals/' + re.escape(name) + r'"\s*:\s*"(https://[^"@]+)@([0-9a-f]{40})"', (skia / 'DEPS').read_text())
        if not match:
            raise ValueError('Pinned dependency not found: ' + name)
        url, commit = match.groups()
        fetch(skia / 'third_party/externals' / name, url, commit)
        deps[name] = {'url': url, 'commit': commit}
    patch_platform(skia)
    gn = skia / 'bin/gn'
    if not gn.is_file():
        run(['python3', 'bin/fetch-gn'], cwd=skia)
    compiler = dk / 'devkitA64/bin'
    gn_args = {
        'target_os': 'linux', 'target_cpu': 'arm64',
        'is_official_build': True, 'is_static_skiasharp': True,
        'cc': str(compiler / 'aarch64-none-elf-gcc'),
        'cxx': str(compiler / 'aarch64-none-elf-g++'),
        'ar': str(compiler / 'aarch64-none-elf-ar'),
        'host_cc': '/usr/bin/gcc', 'host_cxx': '/usr/bin/g++', 'host_ar': '/usr/bin/ar',
        'skia_enable_ganesh': False, 'skia_enable_graphite': False,
        'skia_use_gl': False, 'skia_use_vulkan': False, 'skia_use_x11': False,
        'skia_use_fontconfig': False, 'skia_use_icu': False, 'skia_use_harfbuzz': False,
        'skia_use_piex': False, 'skia_use_dng_sdk': False,
        'skia_use_lua': False, 'skia_use_perfetto': False,
        'skia_enable_tools': False,
        'skia_use_system_expat': False, 'skia_use_system_freetype2': False,
        'skia_use_system_libjpeg_turbo': False, 'skia_use_system_libpng': False,
        'skia_use_system_libwebp': False, 'skia_use_system_zlib': False,
        'extra_cflags': ['-D__SWITCH__', '-DSK_BUILD_FOR_UNIX', '-D_GNU_SOURCE=1', '-march=armv8-a+crc+crypto',
                         '-mtune=cortex-a57', '-mtp=soft'],
    }
    encoded = ' '.join(k + '=' + json.dumps(v) for k, v in gn_args.items())
    build = skia / 'out/nx'
    run([gn, 'gen', build, '--args=' + encoded], cwd=skia)
    run(['ninja', '-C', build, '-j', args.jobs, 'SkiaSharp'], cwd=skia)
    archive = build / 'libSkiaSharp.a'
    if not archive.is_file():
        raise ValueError('Expected the static SkiaSharp archive')
    symbols = subprocess.check_output([compiler / 'aarch64-none-elf-nm', '-g', '--defined-only', archive], text=True)
    exports = sorted(set(re.findall(r'\b[TW]\s+((?:sk_|gr_|skottie_|sksg_|skresources_)[A-Za-z0-9_]+)\s*$', symbols, re.M)))
    for required in ['sk_version_get_milestone', 'sk_bitmap_new', 'sk_canvas_draw_simple_text',
                     'sk_typeface_create_from_file', 'sk_font_new_with_values', 'sk_pngencoder_encode']:
        if required not in exports:
            raise ValueError('Required C API export missing: ' + required)
    output.mkdir(parents=True, exist_ok=True)
    shutil.copy2(archive, output / archive.name)
    # The C resolver uses addresses of real functions from the verified archive.
    resolver = '/* Generated real SkiaSharp C API exports. */\n#include <string.h>\n'
    resolver += ''.join('extern void ' + name + '(void);\n' for name in exports)
    resolver += 'void *wog_skia_symbol(const char *name) {\n'
    resolver += ''.join('  if (!strcmp(name, "' + name + '")) return (void *)' + name + ';\n' for name in exports)
    resolver += '  return 0;\n}\n'
    (output / 'skia_exports.c').write_text(resolver)
    (output / 'exports.json').write_text(json.dumps(exports, indent=2) + '\n')
    licenses = output / 'licenses'
    licenses.mkdir(exist_ok=True)
    shutil.copy2(skia / 'LICENSE', licenses / 'Skia-LICENSE')
    for name in DEPENDENCIES:
        for file in (skia / 'third_party/externals' / name).iterdir():
            if file.is_file() and re.match(r'(LICENSE|COPYING|COPYRIGHT|FTL)', file.name, re.I):
                shutil.copy2(file, licenses / (name + '-' + file.name))
    manifest = {'skia_commit': SKIA_COMMIT, 'dependencies': deps, 'gn_args': gn_args,
                'compiler': subprocess.check_output([compiler / 'aarch64-none-elf-g++', '--version'], text=True).splitlines()[0],
                'archive_bytes': archive.stat().st_size,
                'archive_sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
                'exports': len(exports), 'hardware_tested': False}
    (output / 'build-info.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print('Built real Switch SkiaSharp archive:', archive.stat().st_size, 'bytes;', len(exports), 'C exports')


if __name__ == '__main__':
    main()
