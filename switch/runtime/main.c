/* WogSwitch managed host, EGL/input bridge, native Skia and game load checks. */
#include "core.h"
#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <GLES3/gl3.h>
#include <stdarg.h>
#include <stddef.h>
#include <stdint.h>
#include <sys/stat.h>
#include <unistd.h>
#include <reent.h>
#include <sys/iosupport.h>
#include "audio.h"
#include "audio_pcm.h"
#include "raster.h"

#ifndef WOG_CHECKPOINT_VERSION
#define WOG_CHECKPOINT_VERSION "0.3.0"
#endif

#define ROOT "sdmc:/switch/WogSwitchProbe"
#define WOG_HANDLE ((void *)(uintptr_t)0xABC02000)
#if defined(WOG_HAS_SKIA)
#define SKIA_HANDLE ((void *)(uintptr_t)0xABC02001)
extern void *wog_skia_symbol(const char *name);
#endif
static FILE *report;
static PadState pad;
static bool graphics_active, stop_requested;
static const devoptab_t *text_console_device;
static Thread *text_console_owner;
static bool text_console_live;
static EGLDisplay display = EGL_NO_DISPLAY;
static EGLSurface surface = EGL_NO_SURFACE;
static EGLContext context = EGL_NO_CONTEXT;
static int width = 1280, height = 720, is_gles;
static void (*clear_color)(float, float, float, float);
static void (*clear_buffer)(unsigned);
static void (*viewport)(int, int, int, int);
static GLuint panel_program, panel_texture, panel_vao;
static uint32_t expected_pixel;
static bool readback_checked;
#define PANEL_GL(X) \
    X(PFNGLCREATESHADERPROC, CreateShader) X(PFNGLSHADERSOURCEPROC, ShaderSource) \
    X(PFNGLCOMPILESHADERPROC, CompileShader) X(PFNGLGETSHADERIVPROC, GetShaderiv) \
    X(PFNGLGETSHADERINFOLOGPROC, GetShaderInfoLog) X(PFNGLDELETESHADERPROC, DeleteShader) \
    X(PFNGLCREATEPROGRAMPROC, CreateProgram) X(PFNGLATTACHSHADERPROC, AttachShader) \
    X(PFNGLLINKPROGRAMPROC, LinkProgram) X(PFNGLGETPROGRAMIVPROC, GetProgramiv) \
    X(PFNGLGETPROGRAMINFOLOGPROC, GetProgramInfoLog) X(PFNGLDELETEPROGRAMPROC, DeleteProgram) \
    X(PFNGLUSEPROGRAMPROC, UseProgram) X(PFNGLGENTEXTURESPROC, GenTextures) \
    X(PFNGLBINDTEXTUREPROC, BindTexture) X(PFNGLTEXPARAMETERIPROC, TexParameteri) \
    X(PFNGLTEXIMAGE2DPROC, TexImage2D) X(PFNGLDELETETEXTURESPROC, DeleteTextures) \
    X(PFNGLPIXELSTOREIPROC, PixelStorei) X(PFNGLGENVERTEXARRAYSPROC, GenVertexArrays) \
    X(PFNGLBINDVERTEXARRAYPROC, BindVertexArray) X(PFNGLDELETEVERTEXARRAYSPROC, DeleteVertexArrays) \
    X(PFNGLGETUNIFORMLOCATIONPROC, GetUniformLocation) X(PFNGLUNIFORM1IPROC, Uniform1i) \
    X(PFNGLACTIVETEXTUREPROC, ActiveTexture) X(PFNGLDRAWARRAYSPROC, DrawArrays) \
    X(PFNGLREADPIXELSPROC, ReadPixels) X(PFNGLGETERRORPROC, GetError) \
    X(PFNGLGETINTEGERVPROC, GetIntegerv) X(PFNGLGETBOOLEANVPROC, GetBooleanv) \
    X(PFNGLISENABLEDPROC, IsEnabled) X(PFNGLENABLEPROC, Enable) X(PFNGLDISABLEPROC, Disable) \
    X(PFNGLCOLORMASKPROC, ColorMask) X(PFNGLBINDFRAMEBUFFERPROC, BindFramebuffer)
#define DECLARE_GL(type, name) static type p_##name;
PANEL_GL(DECLARE_GL)
#undef DECLARE_GL
static struct {
    bool active;
    GLint program, vao, draw_fb, read_fb, texture_unit, texture0, viewport[4], unpack_align, unpack_row;
    GLboolean flags[5], color_mask[4];
} dialog_state;
static const GLenum dialog_caps[] = { GL_BLEND, GL_DEPTH_TEST, GL_STENCIL_TEST, GL_SCISSOR_TEST, GL_CULL_FACE };

void wog_log(const char *text)
{
    const char *line = text ? text : "(null)";
    if (report) {
        static unsigned routine_lines;
        fprintf(report, "%s\n", line);
        bool routine = strncmp(line,"PERF ",5)==0 || strncmp(line,"DETAIL ",7)==0 || strncmp(line,"AUDIO supply ",13)==0;
        if (!routine || (__atomic_add_fetch(&routine_lines,1,__ATOMIC_RELAXED)&7u)==0) fflush(report);
    }
    svcOutputDebugString(line, strlen(line));
    if (!graphics_active) {
        // stdout stays on the safe file sink. Mirror only our main-thread
        // progress directly to the live text renderer, never via printf.
        if (threadGetSelf() == text_console_owner && text_console_device &&
            text_console_live && text_console_device->write_r) {
            text_console_device->write_r(_REENT, NULL, line, strlen(line));
            text_console_device->write_r(_REENT, NULL, "\n", 1);
            consoleUpdate(NULL);
        }
        if (io_has_stdio_redirection()) printf("%s\n", line);
    }
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
        if (context != EGL_NO_CONTEXT) {
            if (panel_texture && p_DeleteTextures) p_DeleteTextures(1, &panel_texture);
            if (panel_program && p_DeleteProgram) p_DeleteProgram(panel_program);
            if (panel_vao && p_DeleteVertexArrays) p_DeleteVertexArrays(1, &panel_vao);
            panel_texture = panel_program = panel_vao = 0;
        }
        eglMakeCurrent(display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
        if (context != EGL_NO_CONTEXT) eglDestroyContext(display, context);
        if (surface != EGL_NO_SURFACE) eglDestroySurface(display, surface);
        eglTerminate(display);
    }
    context = EGL_NO_CONTEXT;
    surface = EGL_NO_SURFACE;
    display = EGL_NO_DISPLAY;
    // Do not reopen the software framebuffer after EGL teardown.
    // Completion/errors remain in the persistent log; return through hbloader.
    text_console_live = false;
    graphics_active = false;
}

int wog_graphics_start(void)
{
    if (!io_has_stdio_redirection()) {
        stage("FAIL refusing EGL transition without safe native stdio redirection");
        return 0;
    }
    stage("Creating EGL context (ES 3, desktop GL 3.3 fallback)...");
    /* Do not write to libnx's disposed console while EGL owns the NWindow. */
    graphics_active = true;
    text_console_live = false;
    console_dispose();
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

static GLuint panel_shader(GLenum kind, const char *body)
{
    const char *parts[] = { is_gles ? "#version 300 es\nprecision highp float;\n" : "#version 330 core\n", body };
    GLuint shader = p_CreateShader(kind);
    p_ShaderSource(shader, 2, parts, NULL);
    p_CompileShader(shader);
    GLint ok = 0;
    p_GetShaderiv(shader, GL_COMPILE_STATUS, &ok);
    if (!ok) {
        char message[1024];
        p_GetShaderInfoLog(shader, sizeof(message), NULL, message);
        stage("FAIL bitmap shader: %s", message);
        p_DeleteShader(shader);
        return 0;
    }
    return shader;
}

static int panel_init(void)
{
#define LOAD_GL(type, name) do { p_##name = (type)wog_gl_proc("gl" #name); if (!p_##name) { stage("FAIL GL function: gl" #name); return 0; } } while (0);
    PANEL_GL(LOAD_GL)
#undef LOAD_GL
    GLuint vs = panel_shader(GL_VERTEX_SHADER,
        "out vec2 uv; void main(){ vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);"
        "uv=p; gl_Position=vec4(p*2.0-1.0,0,1); }");
    GLuint fs = panel_shader(GL_FRAGMENT_SHADER,
        "in vec2 uv; uniform sampler2D image; out vec4 color;"
        "void main(){ color=texture(image,vec2(uv.x,1.0-uv.y)); }");
    if (!vs || !fs) { if (vs) p_DeleteShader(vs); if (fs) p_DeleteShader(fs); return 0; }
    panel_program = p_CreateProgram();
    p_AttachShader(panel_program, vs); p_AttachShader(panel_program, fs);
    p_LinkProgram(panel_program);
    p_DeleteShader(vs); p_DeleteShader(fs);
    GLint linked = 0;
    p_GetProgramiv(panel_program, GL_LINK_STATUS, &linked);
    if (!linked) {
        char message[1024]; p_GetProgramInfoLog(panel_program, sizeof(message), NULL, message);
        stage("FAIL bitmap program: %s", message); return 0;
    }
    p_UseProgram(panel_program);
    p_Uniform1i(p_GetUniformLocation(panel_program, "image"), 0);
    p_GenVertexArrays(1, &panel_vao);
    p_GenTextures(1, &panel_texture);
    p_ActiveTexture(GL_TEXTURE0); p_BindTexture(GL_TEXTURE_2D, panel_texture);
    p_TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
    p_TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    p_TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    p_TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
    return 1;
}

int wog_upload_bitmap(const void *pixels, int w, int h, int row_bytes)
{
    if (!graphics_active || !pixels || w <= 10 || h <= 10 || row_bytes < w * 4 || row_bytes % 4) return 0;
    if (!panel_program && !panel_init()) return 0;
    for (int i = 0; i < 8 && p_GetError() != GL_NO_ERROR; i++) { }
    p_ActiveTexture(GL_TEXTURE0); p_BindTexture(GL_TEXTURE_2D, panel_texture);
    p_PixelStorei(GL_UNPACK_ALIGNMENT, 4);
    p_PixelStorei(GL_UNPACK_ROW_LENGTH, row_bytes / 4);
    p_TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, pixels);
    p_PixelStorei(GL_UNPACK_ROW_LENGTH, 0);
    memcpy(&expected_pixel, (const uint8_t *)pixels + 10 * row_bytes + 10 * 4, 4);
    GLenum error = p_GetError();
    if (error) stage("FAIL bitmap upload GL error: 0x%x", error);
    return error == GL_NO_ERROR;
}

int wog_dialog_begin(void)
{
    if (!graphics_active || dialog_state.active || (!panel_program && !panel_init())) return 0;
    p_GetIntegerv(GL_CURRENT_PROGRAM, &dialog_state.program);
    p_GetIntegerv(GL_VERTEX_ARRAY_BINDING, &dialog_state.vao);
    p_GetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &dialog_state.draw_fb);
    p_GetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &dialog_state.read_fb);
    p_GetIntegerv(GL_ACTIVE_TEXTURE, &dialog_state.texture_unit);
    p_ActiveTexture(GL_TEXTURE0); p_GetIntegerv(GL_TEXTURE_BINDING_2D, &dialog_state.texture0);
    p_GetIntegerv(GL_VIEWPORT, dialog_state.viewport);
    p_GetIntegerv(GL_UNPACK_ALIGNMENT, &dialog_state.unpack_align);
    p_GetIntegerv(GL_UNPACK_ROW_LENGTH, &dialog_state.unpack_row);
    p_GetBooleanv(GL_COLOR_WRITEMASK, dialog_state.color_mask);
    for (unsigned i = 0; i < sizeof(dialog_caps)/sizeof(dialog_caps[0]); i++) dialog_state.flags[i] = p_IsEnabled(dialog_caps[i]);
    dialog_state.active = true;
    return 1;
}

void wog_dialog_end(void)
{
    if (!dialog_state.active) return;
    p_UseProgram(dialog_state.program); p_BindVertexArray(dialog_state.vao);
    p_BindFramebuffer(GL_DRAW_FRAMEBUFFER, dialog_state.draw_fb);
    p_BindFramebuffer(GL_READ_FRAMEBUFFER, dialog_state.read_fb);
    p_ActiveTexture(GL_TEXTURE0); p_BindTexture(GL_TEXTURE_2D, dialog_state.texture0);
    p_ActiveTexture(dialog_state.texture_unit);
    viewport(dialog_state.viewport[0], dialog_state.viewport[1], dialog_state.viewport[2], dialog_state.viewport[3]);
    p_PixelStorei(GL_UNPACK_ALIGNMENT, dialog_state.unpack_align); p_PixelStorei(GL_UNPACK_ROW_LENGTH, dialog_state.unpack_row);
    p_ColorMask(dialog_state.color_mask[0], dialog_state.color_mask[1], dialog_state.color_mask[2], dialog_state.color_mask[3]);
    for (unsigned i = 0; i < sizeof(dialog_caps)/sizeof(dialog_caps[0]); i++) {
        if (dialog_state.flags[i]) p_Enable(dialog_caps[i]); else p_Disable(dialog_caps[i]);
    }
    dialog_state.active = false;
}

void wog_draw_bitmap(void)
{
    if (!graphics_active || !panel_program) return;
    p_BindFramebuffer(GL_FRAMEBUFFER, 0);
    for (unsigned i = 0; i < sizeof(dialog_caps)/sizeof(dialog_caps[0]); i++) p_Disable(dialog_caps[i]);
    p_ColorMask(GL_TRUE, GL_TRUE, GL_TRUE, GL_TRUE);
    viewport(0, 0, width, height);
    p_UseProgram(panel_program); p_BindVertexArray(panel_vao);
    p_ActiveTexture(GL_TEXTURE0); p_BindTexture(GL_TEXTURE_2D, panel_texture);
    p_DrawArrays(GL_TRIANGLES, 0, 3);
    if (!readback_checked) {
        uint32_t actual = 0;
        p_ReadPixels(10, height - 11, 1, 1, GL_RGBA, GL_UNSIGNED_BYTE, &actual);
        if (actual == expected_pixel) stage("PASS GPU bitmap readback: CPU/GPU RGBA match");
        else { stage("FAIL GPU bitmap readback: expected 0x%08x, got 0x%08x", expected_pixel, actual); stop_requested = true; }
        readback_checked = true;
    }
}

int wog_swap(void)
{
    if (!graphics_active) return 0;
    if (!eglSwapBuffers(display, surface)) {
        stage("FAIL eglSwapBuffers: 0x%x", eglGetError());
        stop_requested = true;
        return 0;
    }
    return 1;
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
#if defined(WOG_HAS_SKIA)
    if (name && (!strcmp(name, "libSkiaSharp") || !strcmp(name, "libSkiaSharp.so"))) return SKIA_HANDLE;
#endif
    if (name && strcmp(name, "WogSwitch") == 0) return WOG_HANDLE;
    return dlshim_loadLibrary(name, flags, err, user);
}

static void *bridge_symbol(void *handle, const char *name, char **err, void *user)
{
#if defined(WOG_HAS_SKIA)
    if (handle == SKIA_HANDLE) {
        void *symbol = wog_skia_symbol(name);
        if (!symbol) stage("FAIL missing native SkiaSharp export: %s", name);
        return symbol;
    }
#endif
    if (handle != WOG_HANDLE) return dlshim_getSymbol(handle, name, err, user);
#define EXPORT(fn) if (strcmp(name, #fn) == 0) return (void *)(fn)
    EXPORT(wog_log); EXPORT(wog_graphics_start); EXPORT(wog_graphics_stop);
    EXPORT(wog_graphics_frame); EXPORT(wog_gl_proc); EXPORT(wog_swap);
    EXPORT(wog_running); EXPORT(wog_width); EXPORT(wog_height); EXPORT(wog_is_gles); EXPORT(wog_pad);
    EXPORT(wog_upload_bitmap); EXPORT(wog_draw_bitmap);
    EXPORT(wog_dialog_begin); EXPORT(wog_dialog_end);
    EXPORT(wog_audio_rdft); EXPORT(wog_ui_scaled); EXPORT(wog_moses_fade); EXPORT(wog_mix_voice); EXPORT(wog_audio_open); EXPORT(wog_audio_write); EXPORT(wog_audio_played);
    EXPORT(wog_audio_flush); EXPORT(wog_audio_close); EXPORT(wog_blit_opaque);
    EXPORT(wog_draw_frame); EXPORT(wog_fill_rect); EXPORT(wog_blit_masked); EXPORT(wog_field_backdrop); EXPORT(wog_transform); EXPORT(wog_blit_scaled); EXPORT(wog_field_wipe);
#undef EXPORT
    stage("FAIL unknown bridge export: %s", name);
    return NULL;
}

static void finish_runtime(void)
{
    wog_audio_shutdown();
    if (graphics_active) wog_graphics_stop();
    text_console_live = false;
    console_dispose();
    if (report) fflush(report);
    application_force_exit();
}

int main(int argc, char **argv)
{
    (void)argc; (void)argv;
    mkdir("sdmc:/switch", 0777);
    mkdir(ROOT, 0777);
    report = fopen(ROOT "/probe.log", "w");
    console_ensure_init();
    text_console_device = devoptab_list[STD_OUT];
    text_console_owner = threadGetSelf();
    text_console_live = true;
#if defined(WOG_GAME_LOADER)
    stage("WogSwitch checkpoint %s - platform patches and optional real game startup", WOG_CHECKPOINT_VERSION);
#elif defined(WOG_HAS_SKIA)
    stage("WogSwitch checkpoint 0.3 - native SkiaSharp image/Korean rendering; NOT a playable game");
#else
    stage("WogSwitch checkpoint 0.2 - managed methods + EGL/input; NOT a playable game");
#endif
    if (!report) { stage("FAIL cannot write probe.log"); goto wait; }
#if defined(WOG_GAME_LOADER)
    if (chdir("sdmc:/") != 0) { stage("FAIL select SD root for absolute managed paths"); goto wait; }
    mkdir(ROOT "/tmp", 0777);
    setenv("TMPDIR", "/switch/WogSwitchProbe/tmp", 1);
    // The Unix BCL otherwise calls getpwuid_r, which Horizon does not provide.
    // These are the application's runtime paths, not the build machine's HOME.
    mkdir(ROOT "/home", 0777);
    mkdir(ROOT "/home/.config", 0777);
    mkdir(ROOT "/home/.cache", 0777);
    mkdir(ROOT "/home/.local", 0777);
    mkdir(ROOT "/home/.local/share", 0777);
    setenv("HOME", "/switch/WogSwitchProbe/home", 1);
    setenv("XDG_CONFIG_HOME", "/switch/WogSwitchProbe/home/.config", 1);
    setenv("XDG_CACHE_HOME", "/switch/WogSwitchProbe/home/.cache", 1);
    setenv("XDG_DATA_HOME", "/switch/WogSwitchProbe/home/.local/share", 1);
    stage("Managed path root: /switch/WogSwitchProbe on sdmc:/");
#endif
    if (!application_initialize(ROOT "/probe.ini")) { stage("FAIL runtime configuration"); goto wait; }
    // System.Native.SysLog uses printf directly, bypassing Mono's log handler
    // and wog_log's graphics guard. Never leave fd 1/2 on the console device
    // when console_dispose releases its framebuffer for EGL.
    if (!io_has_stdio_redirection() && io_stdio_to_file(ROOT "/runtime.log") < 0) {
        stage("FAIL cannot redirect native stdout/stderr; game startup blocked");
        goto wait;
    }
    stage("PASS native stdout/stderr redirected before graphics; see runtime.log");
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
    finish_runtime();
    return 0;
wait:
    wog_audio_shutdown();
    if (graphics_active) wog_graphics_stop();
    stage("Press PLUS to exit. Results: switch/WogSwitchProbe/probe.log");
    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);
    while (appletMainLoop()) {
        padUpdate(&pad);
        if (padGetButtons(&pad) & HidNpadButton_Plus) break;
        if (text_console_live) consoleUpdate(NULL);
    }
    finish_runtime();
    return 0;
}
