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

    public static int Main(string[] args)
    {
        desktop = args.Contains("--desktop-smoke");
        try
        {
            Log("Managed checkpoint 0.2: original Wog DLL method execution");
            string root = desktop ? Path.GetTempPath() : "sdmc:/switch/WogSwitchProbe";
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
                Log("PASS DESKTOP SMOKE COMPLETE");
                return 0;
            }

            Log("Testing native graphics context...");
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
                Log("DISPLAY TEST: blue/green background, red while A is pressed. PLUS exits.");
                Log("SkiaSharp native rendering, game assets and audio are not exercised by this checkpoint.");
                bool padLogged = false;
                while (Native.wog_running() != 0)
                {
                    var state = host.ReadPad();
                    bool pressed = (state.Buttons & 0x1000) != 0;
                    if (pressed && !padLogged)
                    {
                        Log("PASS controller input: Switch A reached WogShim XInput.State");
                        padLogged = true;
                    }
                    Native.wog_graphics_frame(pressed ? 0.8f : 0.05f, pressed ? 0.08f : 0.32f, pressed ? 0.08f : 0.65f);
                    host.SwapBuffers();
                }
                Log("PASS managed graphics/input loop completed");
            }
            finally { Native.wog_graphics_stop(); }
            return 0;
        }
        catch (Exception ex)
        {
            Log("FAIL managed checkpoint: " + ex);
            return 1;
        }
    }

    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException(check);
    }

    internal static void Log(string line)
    {
        if (desktop) Console.WriteLine(line);
        else Native.wog_log(line);
    }
}

internal sealed class SwitchHost(bool desktop) : IHost
{
    public (int W, int H) SurfaceSize => desktop ? (640, 360) : (Native.wog_width(), Native.wog_height());
    public bool IsGles => !desktop && Native.wog_is_gles() != 0;
    public bool ListDrives => false;
    public bool HasTouch => false;
    public nint GetProcAddress(string name) => Native.wog_gl_proc(name);
    public void SwapBuffers() => Native.wog_swap();
    public void PumpEvents() => Native.wog_running();
    public void ShowMessage(string caption, string text) => Program.Log(caption + ": " + text);
    public int Ask(string caption, string text, string[] buttons) =>
        throw new NotSupportedException("Interactive dialogs are not implemented in checkpoint 0.2.");
    public string PickFolder(string description) =>
        throw new NotSupportedException("Game folder selection is not implemented in checkpoint 0.2.");
    public IAudioSink OpenAudio(int sampleRate, int channels, int bitsPerSample) =>
        throw new NotSupportedException("Audio is not implemented in checkpoint 0.2.");

    internal XInput.State ReadPad()
    {
        Native.wog_pad(out Native.Pad p);
        return new XInput.State { PacketNumber = p.Packet, Buttons = p.Buttons,
            LeftTrigger = p.LeftTrigger, RightTrigger = p.RightTrigger,
            ThumbLX = p.LX, ThumbLY = p.LY, ThumbRX = p.RX, ThumbRY = p.RY };
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
    internal static extern void wog_swap();
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
}
