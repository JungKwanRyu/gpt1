#!/usr/bin/env python3
"""Run actual native teardown and pinned SDK exit code with mocked EGL/libnx."""
import argparse
from pathlib import Path
import subprocess
import tempfile
from test_runtime_stdio import function


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--nroforge', type=Path, required=True)
    args = parser.parse_args()
    source = (Path(__file__).resolve().parents[1] / 'switch/runtime/main.c').read_text()
    core = (args.nroforge / 'runner/shared/core.c').read_text()
    harness = r'''
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <setjmp.h>
#include <assert.h>
#define EGL_NO_DISPLAY 0
#define EGL_NO_CONTEXT 0
#define EGL_NO_SURFACE 0
static int display, context, surface;
static unsigned panel_texture, panel_program, panel_vao;
static bool graphics_active, text_console_live, using_console;
static int deleted, terminated, disposed, audio_shutdown, exit_calls;
static FILE *report;
static jmp_buf finished;
static uint32_t __nx_applet_exit_mode;
static void delete_many(int n, const unsigned *p) { assert(n == 1 && *p); deleted++; }
static void delete_one(unsigned p) { assert(p); deleted++; }
static void (*p_DeleteTextures)(int, const unsigned *) = delete_many;
static void (*p_DeleteVertexArrays)(int, const unsigned *) = delete_many;
static void (*p_DeleteProgram)(unsigned) = delete_one;
static void eglMakeCurrent(int d, int a, int b, int c) { assert(d && !a && !b && !c); }
static void eglDestroyContext(int d, int c) { assert(d && c); }
static void eglDestroySurface(int d, int s) { assert(d && s); }
static void eglTerminate(int d) { assert(d); terminated++; }
static void consoleExit(void *p) { (void)p; disposed++; }
static void wog_audio_shutdown(void) { audio_shutdown++; }
static void test_exit(int code) { assert(code == 0); exit_calls++; longjmp(finished, 1); }
#define exit test_exit
'''
    harness += '\n'.join([
        function(core, 'console_dispose'), function(core[core.index('void application_force_exit()'):], 'application_force_exit'),
        function(source, 'wog_graphics_stop'), function(source, 'finish_runtime')])
    harness += r'''
int main(void) {
    report = tmpfile(); assert(report);
    display = context = surface = 1;
    panel_texture = panel_program = panel_vao = 1;
    graphics_active = true; text_console_live = false; using_console = false;
    if (!setjmp(finished)) finish_runtime();
    assert(!graphics_active && !text_console_live && !using_console);
    assert(!display && !context && !surface && !panel_texture && !panel_program && !panel_vao);
    assert(deleted == 3 && terminated == 1 && disposed == 0 && audio_shutdown == 1);
    assert(exit_calls == 1 && __nx_applet_exit_mode == 1);
    /* A failed/partial EGL start and repeated stop must not reacquire a window. */
    display = 1; context = surface = 0; graphics_active = true;
    wog_graphics_stop(); wog_graphics_stop();
    assert(terminated == 2 && deleted == 3 && disposed == 0 && !text_console_live);
    /* Failure before EGL still disposes the original live console once. */
    using_console = text_console_live = true; __nx_applet_exit_mode = 0;
    if (!setjmp(finished)) finish_runtime();
    assert(disposed == 1 && !text_console_live && !using_console && exit_calls == 2 && __nx_applet_exit_mode == 1);
    fclose(report);
    puts("PASS actual native EGL teardown never reopens/updates the software framebuffer; normal and pre-EGL exits use the SDK clean applet exit exactly once");
    return 0;
}
'''
    with tempfile.TemporaryDirectory(prefix='wog-exit-') as tmp:
        path = Path(tmp)
        (path / 'test.c').write_text(harness)
        subprocess.run(['gcc', '-std=c17', '-O2', '-Wall', '-Wextra', '-Werror',
                        '-fsanitize=undefined', '-fno-sanitize-recover=all',
                        str(path / 'test.c'), '-o', str(path / 'test')], check=True)
        subprocess.run([str(path / 'test')], check=True)
    between = source.split('int result = mono_jit_exec(', 1)[1].split('wait:', 1)[0]
    assert 'finish_runtime();' in between and 'consoleUpdate' not in between


if __name__ == '__main__':
    main()
