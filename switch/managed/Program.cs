using System.Runtime.InteropServices;
using Mono.Cecil;
using SkiaSharp;
using WogPatcher;
using WogShim;
using WogShim.Gpu;
using WogShim.Win32;

namespace WogSwitch;

internal static class Program
{
    private static bool desktop;
    private static readonly System.Diagnostics.Stopwatch patchClock = System.Diagnostics.Stopwatch.StartNew();

    public static int Main(string[] args)
    {
        desktop = args.Contains("--desktop-smoke");
        try
        {
            // This supported runtime switch avoids resource-manager lookups
            // when the embedded runtime formats its own error messages.
            AppContext.SetSwitch("System.Resources.UseSystemResourceKeys", true);
            Log("Managed checkpoint 0.5.5: visible patch phases and current-run load journal");
            string root = desktop ? Path.GetTempPath() : "/switch/WogSwitchProbe";
            var layout = new GameLayout(Path.Combine(root, "release"), Path.Combine(root, "patched"),
                Path.Combine(root, "save"), null, Path.Combine(root, "managed/WogShim.dll"));
            Require(layout.SaveDir.EndsWith("save"), "GameLayout constructor and SaveDir getter");
            Require(layout == layout with { }, "GameLayout record clone and equality");
            Log("PASS WogPatcher method execution: GameLayout constructor/getter/clone/equality");

            var host = new SwitchHost(desktop);
            Host.LogSink = Log;
            Host.Use(host);
            Require(ReferenceEquals(Host.Current, host), "Host.Use / Host.Current");
            Log("PASS WogShim method execution: Host.Use and Host.Current");

            string shimPath = Path.Combine(AppContext.BaseDirectory, "WogShim.dll");
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(shimPath))
                Require(assembly.Name.Name == "WogShim", "Mono.Cecil original DLL inspection");
            Log("PASS Mono.Cecil 0.11.6: original WogShim DLL parsed");

            if (desktop)
            {
                using var bitmap = new SKBitmap(64, 32);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.CornflowerBlue);
                Require(bitmap.GetPixel(5, 5) == SKColors.CornflowerBlue, "Skia desktop raster pixels");
                Log("PASS desktop SkiaSharp raster check (does not prove Switch native Skia support)");
                string fontFile = Path.Combine(AppContext.BaseDirectory, "assets/NanumGothic.ttf");
                using var panel = RenderPanel.Create(fontFile, false);
                RenderPanel.Save(panel, Path.Combine(AppContext.BaseDirectory, "render-desktop.png"));
                using var dialog = DialogPanel.Create(fontFile, "창세기전3 원본 폴더 선택", "원본 자료 경로를 확인해 주세요.\n컨트롤러로 선택하고 A로 확인합니다.",
                    new[] { "파트2 폴더 고르기", "파트1 폴더 고르기", "끝내기" }, 1);
                RenderPanel.Save(dialog, Path.Combine(AppContext.BaseDirectory, "render-dialog-desktop.png"));
                Log("PASS DESKTOP SMOKE COMPLETE");
                return 0;
            }

            string[] status;
            bool gameCheckFailed = false;
            LauncherSettings? launchSettings = null;
            try
            {
                Diagnostics.Step("read launcher settings", Log);
                var settings = LauncherSettings.Read(root);
                launchSettings = settings; host.Settings = settings;
                Log("Resolved PC release: " + settings.ReleaseDir);
                Diagnostics.Step("check PC game DLL presence", Log);
                if (settings.HasAnyGameDll)
                {
                    Diagnostics.Step("prepare and load PC game", Log);
                    status = GameLoader.Run(settings, Log);
                }
                else
                {
                    Log("WAIT game DLLs: copy both original PC DLLs into " + settings.ReleaseDir);
                    string instructions = "Waiting for original PC release DLLs.\nMain invoked: no\nRelease: " + settings.ReleaseDir +
                        "\nRequired: WarOfGenesis.dll and WarOfGenesis.Assets.dll\nOriginal game folders are optional for this DLL load check.\n";
                    File.WriteAllText(settings.ReportPath, instructions);
                    status = new[] { "PC 게임 DLL을 기다리는 중", "release/WarOfGenesis.dll", "release/WarOfGenesis.Assets.dll", "두 파일을 복사한 뒤 다시 실행해 주세요" };
                }
            }
            catch (Exception ex)
            {
                gameCheckFailed = true;
                string details = Diagnostics.Describe(ex);
                Log("FAIL game preparation:\n" + details);
                try { File.WriteAllText(Path.Combine(root, "load-report.txt"), "Main invoked: no\nFAIL game preparation:\n" + details); }
                catch (Exception writeError) { Log("FAIL write load-report: " + Diagnostics.Describe(writeError)); }
                status = new[] { "게임 DLL 확인 실패", ex.GetType().Name, "자세한 원인: load-report.txt", "이미지·한글 검사는 이어서 진행합니다" };
            }

            Diagnostics.Step("native graphics context", Log);
            if (Native.wog_graphics_start() == 0)
            {
                Log("FAIL native EGL/OpenGL initialization; see probe.log");
                return 2;
            }
            try
            {
                GlLoader.Load(host.GetProcAddress);
                Log("PASS WogShim GlLoader.Load: managed-to-native GL function pointers");
                XInput.Poll = host.ReadPad;
                string fontFile = Path.Combine(root, "assets/NanumGothic.ttf");
                Fonts.OverrideFile = fontFile;
                using var panel = RenderPanel.Create(fontFile, false, status);
                RenderPanel.Save(panel, Path.Combine(root, "render.png"));
                Require(Native.wog_upload_bitmap(panel.GetPixels(), panel.Width, panel.Height, panel.RowBytes) != 0, "GPU bitmap upload");
                Log("PASS Skia bitmap GPU upload");
                PlatformGameBridge.Notice = text => host.ShowMessage("스위치 안내", text);
                Log("DISPLAY TEST: A changes banner; X invokes real game Main once; PLUS exits.");
                bool lastPressed = false;
                bool padLogged = false;
                bool started = false, lastX = false;
                while (Native.wog_running() != 0)
                {
                    var state = host.ReadPad();
                    bool pressed = (state.Buttons & 0x1000) != 0;
                    bool x = (state.Buttons & 0x4000) != 0;
                    if (x && !lastX && !started && !gameCheckFailed && launchSettings?.HasAnyGameDll == true)
                    {
                        started = true;
                        host.BeginGame();
                        int code;
                        try { code = GameSession.Start(launchSettings, Log, () => host.Frames); }
                        finally { host.EndGame(); }
                        status = new[] { "게임 시작 시도 종료 — code " + code, "게임 호스트 출력 프레임 " + host.Frames,
                            "자세한 결과: game-start.txt", "다시 시도하려면 앱을 재실행해 주세요" };
                        using var resultPanel = RenderPanel.Create(fontFile, false, status);
                        RenderPanel.Save(resultPanel, Path.Combine(root, "render.png"));
                        Require(Native.wog_upload_bitmap(resultPanel.GetPixels(), resultPanel.Width, resultPanel.Height, resultPanel.RowBytes) != 0, "Startup result upload");
                    }
                    lastX = x;
                    if (pressed && !padLogged)
                    {
                        Log("PASS controller input: Switch A reached WogShim XInput.State");
                        padLogged = true;
                    }
                    if (pressed != lastPressed)
                    {
                        using var updated = RenderPanel.Create(fontFile, pressed, status);
                        Require(Native.wog_upload_bitmap(updated.GetPixels(), updated.Width, updated.Height, updated.RowBytes) != 0, "Updated bitmap upload");
                        lastPressed = pressed;
                    }
                    Native.wog_draw_bitmap();
                    host.SwapBuffers();
                }
                Log("PASS managed graphics/input loop completed");
            }
            finally { Native.wog_graphics_stop(); }
            return gameCheckFailed ? 3 : 0;
        }
        catch (Exception ex)
        {
            Log("FAIL managed checkpoint:\n" + Diagnostics.Describe(ex));
            return 1;
        }
    }

    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException(check);
    }

    internal static void Log(string line)
    {
        if (line.StartsWith("PATCH ", StringComparison.Ordinal)) line += " [elapsed " + (long)patchClock.Elapsed.TotalSeconds + "s]";
        if (desktop) Console.WriteLine(line);
        else Native.wog_log(line);
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Pad
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
    }

    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wog_log([MarshalAs(UnmanagedType.LPUTF8Str)] string line);
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int wog_graphics_start();
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wog_graphics_stop();
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wog_graphics_frame(float r, float g, float b);
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint wog_gl_proc([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int wog_swap();
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int wog_running();
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int wog_width();
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int wog_height();
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int wog_is_gles();
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wog_pad(out Pad pad);
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int wog_upload_bitmap(nint pixels, int width, int height, int rowBytes);
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wog_draw_bitmap();
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int wog_dialog_begin();
    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wog_dialog_end();
}
