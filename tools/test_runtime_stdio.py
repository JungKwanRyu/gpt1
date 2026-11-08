#!/usr/bin/env python3
"""Exercise the pinned NroForge stdio routing with a disposed console sink."""
import argparse
from pathlib import Path
import resource
import signal
import subprocess
import tempfile


def function(source, name):
    start = source.index(name + "(")
    start = source.rfind("\n", 0, start) + 1
    body = source.index("{", start)
    depth = 1
    end = body + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--nroforge", type=Path, required=True)
    args = parser.parse_args()
    source = (args.nroforge / "runner/shared/io_util.c").read_text()
    bodies = "\n".join(function(source, name) for name in [
        "io_has_stdio_redirection", "file_log_write", "redirect_newlib_io", "io_stdio_to_file"])
    native = (Path(__file__).resolve().parents[1] / "switch/runtime/main.c").read_text()
    harness = r'''
#include <stdbool.h>
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <unistd.h>
#define STD_OUT 1
#define STD_ERR 2
struct _reent { int unused; };
typedef ssize_t (*newlib_io_write_callback)(struct _reent *, void *, const char *, size_t);
typedef struct { const char *name; newlib_io_write_callback write_r; } devoptab_t;
static devoptab_t *devoptab_list[3];
static bool isCustomRedirect;
static FILE *file_log_handle;
static int nxlink_log_socket = -1;
static volatile char *console_framebuffer;
static int console_writes;
static ssize_t console_write(struct _reent *r, void *fd, const char *p, size_t n) {
    (void)r; (void)fd; (void)p;
    console_writes++;
    *console_framebuffer = 'x'; /* Same freed/null framebuffer failure boundary. */
    return n;
}
'''
    harness += bodies + r'''
#define _REENT NULL
typedef int Thread;
static Thread main_thread, other_thread;
static Thread *current_thread = &main_thread;
static Thread *threadGetSelf(void) { return current_thread; }
static const devoptab_t *text_console_device;
static Thread *text_console_owner;
static bool text_console_live, graphics_active;
static FILE *report;
static int console_updates;
static void consoleUpdate(void *unused) { (void)unused; console_updates++; }
static void svcOutputDebugString(const char *p, size_t n) { (void)p; (void)n; }
'''
    harness += function(native, "wog_log") + r'''
int main(int argc, char **argv) {
    devoptab_t console = { "console", console_write };
    devoptab_list[STD_OUT] = devoptab_list[STD_ERR] = &console;
    if (argc == 3) {
        char framebuffer;
        console_framebuffer = &framebuffer;
        text_console_device = &console;
        text_console_owner = &main_thread;
        text_console_live = true;
        wog_log("PATCH Retarget visible");
        if (console_writes != 2 || console_updates != 1) return 5;
        graphics_active = true;
        text_console_live = false;
        console_framebuffer = NULL;
        wog_log("must not draw after EGL transition");
        graphics_active = false;
        wog_log("must not draw on disposed renderer");
        text_console_live = true;
        current_thread = &other_thread;
        wog_log("background logging must not draw");
        if (console_writes != 2 || console_updates != 1) return 6;
        return 0;
    }
    if (argc == 2) {
        if (io_stdio_to_file(argv[1]) < 0 || !io_has_stdio_redirection()) return 2;
    }
    console_framebuffer = NULL; /* consoleExit; EGL owns the window now. */
    const char *message = "SYSLOG AFTER CONSOLE EXIT\n";
    if (devoptab_list[STD_OUT]->write_r(NULL, NULL, message, strlen(message)) != (ssize_t)strlen(message)) return 3;
    message = "STDERR AFTER CONSOLE EXIT\n";
    if (devoptab_list[STD_ERR]->write_r(NULL, NULL, message, strlen(message)) != (ssize_t)strlen(message)) return 4;
    /* Exit immediately: ensure newline flush survived without normal cleanup. */
    _Exit(0);
}
'''
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    with tempfile.TemporaryDirectory(prefix="wog-stdio-") as temporary:
        root = Path(temporary)
        (root / "test.c").write_text(harness)
        subprocess.run(["gcc", "-std=c17", "-O0", "-Wall", "-Wextra", "-Werror", "-Wno-unused-parameter", str(root / "test.c"), "-o", str(root / "test")], check=True)
        old = subprocess.run([str(root / "test")], capture_output=True)
        assert old.returncode == -signal.SIGSEGV, old.returncode
        print("PASS old console stdio path reproduces a null framebuffer crash after console exit")
        log = root / "runtime.log"
        subprocess.run([str(root / "test"), str(log)], check=True)
        assert "SYSLOG AFTER CONSOLE EXIT\n" in log.read_text()
        assert "STDERR AFTER CONSOLE EXIT\n" in log.read_text()
        subprocess.run([str(root / "test"), str(log)], check=True)
        assert log.read_text().count("SYSLOG AFTER CONSOLE EXIT") == 2
        print("PASS actual SDK stdio callback captures stdout/stderr after console exit, flushes before abrupt exit and appends across runs")
        subprocess.run([str(root / "test"), "--mirror", "test"], check=True)
        print("PASS real native progress mirror draws only on the live main-thread console; EGL, disposed and background paths never draw")
    code = (Path(__file__).resolve().parents[1] / "switch/runtime/main.c").read_text()
    assert code.index('io_stdio_to_file(ROOT "/runtime.log")') < code.index('mono_jit_init("wog-switch-managed")')
    assert "refusing EGL transition without safe native stdio redirection" in code
    print("PASS native runtime routes stdio before Mono starts and rejects an unsafe EGL transition")


if __name__ == "__main__":
    main()
