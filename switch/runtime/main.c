/* WogSwitch checkpoint 0.2: managed Wog methods + EGL graphics/input bridge.
 * This is a compatibility checkpoint, not a game launcher. */
#include "core.h"
#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <stdarg.h>
#include <stddef.h>
#include <stdint.h>
#include <sys/stat.h>

#define ROOT "sdmc:/switch/WogSwitchProbe"
#define WOG_HANDLE ((void *)(uintptr_t)0xABC02000)
static FILE *report;
static PadState pad;
static bool graphics_active, stop_requested;
static EGLDisplay display = EGL_NO_DISPLAY;
static EGLSurface surface = EGL_NO_SURFACE;
static EGLContext context = EGL_NO_CONTEXT;
static int width = 1280, height = 720, is_gles;
static void (*clear_color)(float, float, float, float);
static void (*clear_buffer)(unsigned);
static void (*viewport)(int, int, int, int);

void wog_log(const char *text)
{
    const char *line = text ? text : "(null)";
    if (report) { fprintf(report, "%s\n", line); fflush(report); }
    svcOutputDebugString(line, strlen(line));
    if (!graphics_active) { printf("%s\n", line); console_update(); }
}

static void stage(const char *format, ...)
{
    char line[1600];
    va_list args;
    va_start(args, format);
    vsnprintf(line, sizeof(line), format, args);
    va_end(args);
    wog_log(line);
}

static void runtime_log(const char *domain, const char *level, const char *message,
                        mono_bool fatal, void *user)
{
    (void)user;
    stage("[mono %s %s%s] %s", domain ? domain : "", level ? level : "",
          fatal ? " FATAL" : "", message ? message : "");
}

int wog_width(void) { return width; }
int wog_height(void) { return height; }
int wog_is_gles(void) { return is_gles; }
void *wog_gl_proc(const char *name) { return (void *)eglGetProcAddress(name); }

static bool create_context(bool gles)
{
    EGLint attributes[] = {
        EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8,
        EGL_SURFACE_TYPE, EGL_WINDOW_BIT,
        EGL_RENDERABLE_TYPE, gles ? EGL_OPENGL_ES3_BIT_KHR : EGL_OPENGL_BIT,
        EGL_NONE
    };
    EGLint count = 0;
    EGLConfig config;
    if (!eglBindAPI(gles ? EGL_OPENGL_ES_API : EGL_OPENGL_API) ||
        !eglChooseConfig(display, attributes, &config, 1, &count) || count < 1)
        return false;
    EGLint es_attributes[] = { EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE };
    EGLint gl_attributes[] = { EGL_CONTEXT_MAJOR_VERSION_KHR, 3, EGL_CONTEXT_MINOR_VERSION_KHR, 3,
        EGL_CONTEXT_OPENGL_PROFILE_MASK_KHR, EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT_KHR, EGL_NONE };
    context = eglCreateContext(display, config, EGL_NO_CONTEXT, gles ? es_attributes : gl_attributes);
    if (context == EGL_NO_CONTEXT) return false;
    surface = eglCreateWindowSurface(display, config, nwindowGetDefault(), NULL);
    if (surface != EGL_NO_SURFACE && eglMakeCurrent(display, surface, surface, context)) {
        is_gles = gles;
        return true;
    }
    if (surface != EGL_NO_SURFACE) eglDestroySurface(display, surface);
    eglDestroyContext(display, context);
    surface = EGL_NO_SURFACE;
    context = EGL_NO_CONTEXT;
    return false;
}

void wog_graphics_stop(void)
{
    if (display != EGL_NO_DISPLAY) {
        eglMakeCurrent(display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
        if (context != EGL_NO_CONTEXT) eglDestroyContext(display, context);
        if (surface != EGL_NO_SURFACE) eglDestroySurface(display, surface);
        eglTerminate(display);
    }
    context = EGL_NO_CONTEXT;
    surface = EGL_NO_SURFACE;
    display = EGL_NO_DISPLAY;
    graphics_active = false;
    console_ensure_init();
}

int wog_graphics_start(void)
{
    stage("Creating EGL context (ES 3, desktop GL 3.3 fallback)...");
    console_dispose();
    /* Do not write to libnx's disposed console while EGL owns the NWindow. */
    graphics_active = true;
    nwindowSetDimensions(nwindowGetDefault(), width, height);
    display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
    EGLint major = 0, minor = 0;
    if (display == EGL_NO_DISPLAY || !eglInitialize(display, &major, &minor)) {
        stage("FAIL eglInitialize: 0x%x", eglGetError());
        wog_graphics_stop();
        return 0;
    }
    if (!create_context(true)) {
        stage("ES 3 unavailable (EGL 0x%x); trying desktop GL", eglGetError());
        if (!create_context(false)) {
            stage("FAIL eglCreateContext/surface/current: 0x%x", eglGetError());
            wog_graphics_stop();
            return 0;
        }
    }
    eglQuerySurface(display, surface, EGL_WIDTH, &width);
    eglQuerySurface(display, surface, EGL_HEIGHT, &height);
    clear_color = wog_gl_proc("glClearColor");
    clear_buffer = wog_gl_proc("glClear");
    viewport = wog_gl_proc("glViewport");
    const unsigned char *(*get_string)(unsigned) = wog_gl_proc("glGetString");
    if (!clear_color || !clear_buffer || !viewport || !get_string) {
        stage("FAIL basic GL entry points");
        wog_graphics_stop();
        return 0;
    }
    stage("PASS EGL %d.%d, %dx%d; GLES=%d", major, minor, width, height, is_gles);
    stage("GPU: %s; GL: %s", get_string(0x1F01), get_string(0x1F02));
    eglSwapInterval(display, 1);
    viewport(0, 0, width, height);
    return 1;
}

void wog_graphics_frame(float r, float g, float b)
{
    if (!graphics_active || !clear_color || !clear_buffer) return;
    clear_color(r, g, b, 1.0f);
    clear_buffer(0x00004000); /* GL_COLOR_BUFFER_BIT */
}

void wog_swap(void)
{
    if (graphics_active && !eglSwapBuffers(display, surface)) {
        stage("FAIL eglSwapBuffers: 0x%x", eglGetError());
        stop_requested = true;
    }
}

int wog_running(void)
{
    if (stop_requested || !appletMainLoop()) return 0;
    padUpdate(&pad);
    return !(padGetButtons(&pad) & HidNpadButton_Plus);
}

typedef struct {
    uint32_t packet;
    uint16_t buttons;
    uint8_t left_trigger, right_trigger;
    int16_t lx, ly, rx, ry;
} WogPad;
_Static_assert(sizeof(WogPad) == 16 && offsetof(WogPad, lx) == 8, "Managed pad ABI mismatch");

void wog_pad(WogPad *out)
{
    static uint32_t packet;
    padUpdate(&pad);
    uint64_t b = padGetButtons(&pad);
    uint16_t mask = 0;
#define MAP(nx, xi) if (b & HidNpadButton_##nx) mask |= xi
    MAP(Up, 0x0001); MAP(Down, 0x0002); MAP(Left, 0x0004); MAP(Right, 0x0008);
    MAP(Plus, 0x0010); MAP(Minus, 0x0020); MAP(StickL, 0x0040); MAP(StickR, 0x0080);
    MAP(L, 0x0100); MAP(R, 0x0200); MAP(A, 0x1000); MAP(B, 0x2000);
    MAP(X, 0x4000); MAP(Y, 0x8000);
#undef MAP
    HidAnalogStickState left = padGetStickPos(&pad, 0), right = padGetStickPos(&pad, 1);
    *out = (WogPad){ ++packet, mask, (b & HidNpadButton_ZL) ? 255 : 0,
        (b & HidNpadButton_ZR) ? 255 : 0, left.x, left.y, right.x, right.y };
}

static void *bridge_load(const char *name, int flags, char **err, void *user)
{
    if (name && strcmp(name, "WogSwitch") == 0) return WOG_HANDLE;
    return dlshim_loadLibrary(name, flags, err, user);
}

static void *bridge_symbol(void *handle, const char *name, char **err, void *user)
{
    if (handle != WOG_HANDLE) return dlshim_getSymbol(handle, name, err, user);
#define EXPORT(fn) if (strcmp(name, #fn) == 0) return (void *)(fn)
    EXPORT(wog_log); EXPORT(wog_graphics_start); EXPORT(wog_graphics_stop);
    EXPORT(wog_graphics_frame); EXPORT(wog_gl_proc); EXPORT(wog_swap);
    EXPORT(wog_running); EXPORT(wog_width); EXPORT(wog_height); EXPORT(wog_is_gles); EXPORT(wog_pad);
#undef EXPORT
    stage("FAIL unknown bridge export: %s", name);
    return NULL;
}

int main(int argc, char **argv)
{
    (void)argc; (void)argv;
    mkdir("sdmc:/switch", 0777);
    mkdir(ROOT, 0777);
    report = fopen(ROOT "/probe.log", "w");
    console_ensure_init();
    stage("WogSwitch checkpoint 0.2 - managed methods + EGL/input; NOT a playable game");
    if (!report) { stage("FAIL cannot write probe.log"); goto wait; }
    if (!application_initialize(ROOT "/probe.ini")) { stage("FAIL runtime configuration"); goto wait; }
    application_configure_mono();
    mono_trace_set_log_handler(runtime_log, NULL);
    mono_dl_fallback_register(bridge_load, bridge_symbol, dlshim_closeLibrary, NULL);
    mono_jit_set_aot_mode(MONO_AOT_MODE_INTERP_ONLY);
    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);
    stage("Starting Mono interpreter and managed checkpoint...");
    MonoDomain *domain = mono_jit_init("wog-switch-managed");
    if (!domain) { stage("FAIL Mono domain"); goto wait; }
    const char *dll = ROOT "/managed/WogSwitch.Managed.dll";
    MonoAssembly *assembly = mono_domain_assembly_open(domain, dll);
    if (!assembly) { stage("FAIL managed host assembly load"); goto wait; }
    char *managed_args[] = { (char *)dll };
    int result = mono_jit_exec(domain, assembly, 1, managed_args);
    stage("Managed checkpoint exit code: %d", result);
wait:
    if (graphics_active) wog_graphics_stop();
    stage("Press PLUS to exit. Results: switch/WogSwitchProbe/probe.log");
    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);
    while (appletMainLoop()) {
        padUpdate(&pad);
        if (padGetButtons(&pad) & HidNpadButton_Plus) break;
        console_update();
    }
    if (report) fclose(report);
    console_dispose();
    return 0;
}
