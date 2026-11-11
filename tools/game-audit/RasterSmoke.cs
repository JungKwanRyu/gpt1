using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WogSwitch;

internal static class RasterSmoke
{
    internal static void Run(Assembly game)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = game.GetType("DuelDx.GameWindow", true)!;
        var window = RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("Cols", flags)!.SetValue(window, 24);
        type.GetField("Rows", flags)!.SetValue(window, 34);
        int width = (int)type.GetProperty("BoardWidth", flags)!.GetValue(window)!;
        int height = (int)type.GetProperty("BoardHeight", flags)!.GetValue(window)!;
        var fb = new uint[width * height];
        type.GetField("_fb", flags)!.SetValue(window, fb);
        var pixel = (Action<int, int, uint>)type.GetMethod("SetPixel", flags)!.CreateDelegate(typeof(Action<int, int, uint>), window);
        var fast = type.GetMethod("SwitchOpaqueBackdrop", flags)!;
        var source = Enumerable.Range(0, 640 * 480).Select(i => unchecked((uint)(i * 1664525 + 1013904223))).ToArray();
        var initial = Enumerable.Range(0, fb.Length).Select(i => unchecked((uint)(i * 17))).ToArray();
        string? library = Environment.GetEnvironmentVariable("WOG_RASTER_TEST_LIB");
        if (string.IsNullOrEmpty(library)) throw new InvalidOperationException("Real native raster test library required");
        nint handle = NativeLibrary.Load(Path.GetFullPath(library));
        NativeLibrary.SetDllImportResolver(typeof(SwitchRaster).Assembly, (name, assembly, path) => name == "WogSwitch" ? handle : 0);
        SwitchRaster.UseNative = true;
        var clear = type.GetMethod("SwitchClearViewport", flags)!;
        int viewW = (int)type.GetProperty("ViewWidth", flags)!.GetValue(window)!;
        int viewH = (int)type.GetProperty("ViewHeight", flags)!.GetValue(window)!;
        foreach (var camera in new[] { (0, 0), (-19, -13), (width - 7, height - 9), (width + 3, height + 2), (35, 71) })
        {
            type.GetField("_camX", flags)!.SetValue(window, camera.Item1);
            type.GetField("_camY", flags)!.SetValue(window, camera.Item2);
            initial.CopyTo(fb, 0);
            int x0 = Math.Clamp(camera.Item1, 0, width), x1 = Math.Clamp(camera.Item1 + viewW, 0, width);
            for (int y = Math.Max(0, camera.Item2); y < Math.Min(height, camera.Item2 + viewH) && x0 < x1; y++)
                fb.AsSpan(y * width + x0, x1 - x0).Fill(0xff14100cu);
            uint[] expected = (uint[])fb.Clone(); initial.CopyTo(fb, 0);
            if (!Equals(clear.Invoke(window, null), true) || !fb.SequenceEqual(expected))
                throw new InvalidDataException("Native viewport clear differs from original clipped span fill");
        }
        SwitchRaster.UseNative = false;
        initial.CopyTo(fb, 0);
        if (!Equals(clear.Invoke(window, null), false) || !fb.SequenceEqual(initial))
            throw new InvalidDataException("Viewport clear fallback modified framebuffer");
        Console.WriteLine("PASS native viewport clear matches original visible rectangle, negative/edge/outside camera and PC fallback");
        foreach (bool native in new[] { false, true })
        {
            SwitchRaster.UseNative = native;
            foreach (var point in new[] { (0, 0), (-21, -17), (width - 10, height - 10), (width, height), (7, 31) })
            {
                initial.CopyTo(fb, 0);
                for (int y = 0; y < 480; y++)
                    for (int x = 0; x < 640; x++) pixel(point.Item1 + x, point.Item2 + y, source[y * 640 + x] | 0xff000000u);
                uint[] expected = (uint[])fb.Clone(); initial.CopyTo(fb, 0);
                fast.Invoke(window, new object[] { source, point.Item1, point.Item2 });
                if (!fb.SequenceEqual(expected)) throw new InvalidDataException("Backdrop differs from actual original SetPixel: native=" + native + " point=" + point);
            }
            initial.CopyTo(fb, 0); fast.Invoke(window, new object?[] { null, 0, 0 });
            if (!fb.SequenceEqual(initial)) throw new InvalidDataException("Null backdrop changed pixels");
        }
        SwitchRaster.UseNative = false;
        Console.WriteLine("PASS actual patched backdrop helper -> real C raster matches original game SetPixel for full frames, clipping, opaque alpha and null backgrounds; managed PC fallback also matches");
    }
}
