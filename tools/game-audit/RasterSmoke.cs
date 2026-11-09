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
        type.GetProperty("Cols", flags)!.SetValue(window, 24);
        type.GetProperty("Rows", flags)!.SetValue(window, 34);
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
