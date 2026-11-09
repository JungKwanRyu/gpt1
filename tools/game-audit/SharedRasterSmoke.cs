using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using WogSwitch;

internal static class SharedRasterSmoke
{
    internal static void Run(Assembly game, string root)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = game.GetType("DuelDx.GameWindow", true)!;
        object window = RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("Cols", flags)!.SetValue(window, 4);
        type.GetField("Rows", flags)!.SetValue(window, 4);
        int width = (int)type.GetProperty("BoardWidth", flags)!.GetValue(window)!;
        int height = (int)type.GetProperty("BoardHeight", flags)!.GetValue(window)!;
        uint[] fb = new uint[width * height]; type.GetField("_fb", flags)!.SetValue(window, fb);
        uint[] initial = Enumerable.Range(0, fb.Length).Select(i => unchecked((uint)(i * 1664525 + 1013904223))).ToArray();
        uint[] source = Enumerable.Range(0, 23 * 19).Select(i => (unchecked((uint)(i * 1664525 + 7654321)) & 0x00ffffffu) |
            (new uint[] { 0, 1, 127, 255 }[i % 4] << 24)).ToArray();
        var draw = type.GetMethod("DrawFrame", flags)!; var original = type.GetMethod("SwitchOriginalDrawFrame", flags)!;
        Type frameType = draw.GetParameters()[0].ParameterType, blendType = draw.GetParameters()[3].ParameterType;
        object frame = Activator.CreateInstance(frameType, flags, null, new object[] { source, 23, 19, -2, 3 }, null)!;
        int cases = 0;
        void Compare(MethodInfo reference, MethodInfo actual, object?[] args)
        {
            initial.CopyTo(fb, 0); reference.Invoke(window, args); uint[] expected = (uint[])fb.Clone();
            initial.CopyTo(fb, 0); actual.Invoke(window, args);
            if (!fb.SequenceEqual(expected)) throw new InvalidDataException("Native shared raster differs: " + actual.Name + "; case=" + cases);
            cases++;
        }
        SwitchRaster.UseNative = true;
        foreach (object? clip in new object?[] { null, (5, 6, 13, 11), (-5, -4, 9, 7), (width + 1, 0, 8, 8) })
        {
            type.GetField("_uiClip", flags)!.SetValue(window, clip);
            foreach (var point in new[] { (0, 0), (-12, -9), (width - 7, height - 5), (width + 20, height) })
                foreach (double fade in new[] { -1.0, 0.0, 0.25, 0.7, 0.999, 1.0, 2.0, double.NaN })
                    for (int mode = 0; mode < 8; mode++)
                        Compare(original, draw, new[] { frame, point.Item1, point.Item2, Enum.ToObject(blendType, mode), (object)fade });
        }
        var fill = type.GetMethod("FillRect", flags)!; var fillOriginal = type.GetMethod("SwitchOriginalFillRect", flags)!;
        foreach (uint color in new uint[] { 0, 0x01010203, 0x7f486712, 0xffac4581 })
            foreach (var p in new[] { (0, 0, width, height), (-9, -4, 30, 21), (width - 2, height - 3, 8, 9), (20, 20, -3, -5) })
                Compare(fillOriginal, fill, new object[] { p.Item1, p.Item2, p.Item3, p.Item4, color });
        var masked = type.GetMethod("BlitMasked", flags)!; var maskedOriginal = type.GetMethod("SwitchOriginalBlitMasked", flags)!;
        foreach (var point in new[] { (0, 0), (-12, -9), (width - 7, height - 5), (width + 20, height) })
            foreach (double fade in new[] { -1.0, 0.0, 0.25, 0.7, 0.999, 1.0, 2.0, double.NaN })
                foreach (bool add in new[] { false, true })
                    Compare(maskedOriginal, masked, new object?[] { source, 23, 19, point.Item1, point.Item2, null, null, fade, add, null });
        // The tinted branch deliberately keeps the original implementation.
        Compare(maskedOriginal, masked, new object?[] { source, 23, 19, 0, 0, (3, 16), null, 0.5, false, null });
        SwitchRaster.UseNative = false;
        Compare(original, draw, new[] { frame, 0, 0, Enum.ToObject(blendType, 2), (object)0.5 });
        FrameProfiler.Reset(root, Console.WriteLine);
        long now = Stopwatch.GetTimestamp();
        FrameProfiler.BeginAt(1, now); FrameProfiler.EndAt(1, now + Stopwatch.Frequency * 14834);
        string report = File.ReadAllText(Path.Combine(root, "performance.log"));
        if (!report.Contains("LONG_GAP phase=upload") || report.Contains("fps=0.00")) throw new InvalidDataException("Suspended interval was reported as ordinary upload/FPS");
        Console.WriteLine("PASS real native shared kernels equal original game DrawFrame/FillRect/BlitMasked: " + cases + " cases, all 8 UI modes, clipping, fade, alpha, additive, tinted/PC fallback");
        Console.WriteLine("PASS long paused upload interval is recorded separately from steady-state FPS");
    }
}
