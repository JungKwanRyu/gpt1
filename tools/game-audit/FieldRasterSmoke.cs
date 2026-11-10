using System.Reflection;
using System.Runtime.CompilerServices;
using WogSwitch;

internal static class FieldRasterSmoke
{
    internal static void Run(Assembly game)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var windowType = game.GetType("DuelDx.GameWindow", true)!;
        object window = RuntimeHelpers.GetUninitializedObject(windowType);
        windowType.GetField("Cols", flags)!.SetValue(window, 24);
        windowType.GetField("Rows", flags)!.SetValue(window, 34);
        windowType.GetField("_lastTime", flags)!.SetValue(window, 12.3);
        int width = (int)windowType.GetProperty("BoardWidth", flags)!.GetValue(window)!;
        int height = (int)windowType.GetProperty("BoardHeight", flags)!.GetValue(window)!;
        uint[] fb = new uint[width * height]; windowType.GetField("_fb", flags)!.SetValue(window, fb);
        uint[] initial = Enumerable.Range(0, fb.Length).Select(i => unchecked((uint)(i * 1664525 + 1013904223))).ToArray();
        var type = game.GetType("DuelDx.FieldScene", true)!;
        object field = RuntimeHelpers.GetUninitializedObject(type);
        type.GetFields(flags).Single(f => f.FieldType == windowType).SetValue(field, window);
        foreach (string name in new[] { "_fieldProps", "_fieldActors" })
        {
            var f = type.GetField(name, flags)!; f.SetValue(field, Activator.CreateInstance(f.FieldType));
        }
        var pieces = new List<(int X1, int Y1, int W, int H, uint[] Px, int PicW, int PicH, int Vx, int Vy)>();
        type.GetField("_fieldPieces", flags)!.SetValue(field, pieces);
        uint[] picture = Enumerable.Range(0, 640 * 480).Select(i => unchecked((uint)(i * 76543))).ToArray();
        uint[] p1 = Enumerable.Range(0, 73 * 51).Select(i => 0x12000000u + (uint)i).ToArray();
        uint[] p2 = Enumerable.Range(0, 47 * 37).Select(i => 0x34000000u + (uint)i * 177).ToArray();
        int cases = 0;
        void Compare(string method, params object[] args)
        {
            initial.CopyTo(fb, 0); var a = type.GetMethod("SwitchOriginal" + method, flags)!.Invoke(field, args);
            var expected = (uint[])fb.Clone(); initial.CopyTo(fb, 0);
            var b = type.GetMethod(method, flags)!.Invoke(field, args);
            if (!Equals(a, b) || !expected.SequenceEqual(fb))
                throw new InvalidDataException("Field pixels/return differ: " + method + "; case=" + cases);
            cases++;
        }
        foreach (bool native in new[] { false, true })
        {
            SwitchRaster.UseNative = native;
            foreach (var dimensions in new[] { (640, 480), (773, 557) })
            {
                var bg = Enumerable.Range(0, dimensions.Item1 * dimensions.Item2)
                    .Select(i => i % 5 < 3 ? (i % 2 == 0 ? 0x12ffff07u : 0x00f8fc00u) : unchecked((uint)(i * 1234567))).ToArray();
                type.GetField("_fieldBg", flags)!.SetValue(field, (bg, dimensions.Item1, dimensions.Item2));
                foreach (bool withPieces in new[] { false, true })
                {
                    pieces.Clear();
                    if (withPieces) {
                        pieces.Add((-35, -17, 550, 440, p1, 73, 51, 2, -3));
                        pieces.Add((250, 30, 600, 550, p2, 47, 37, -1, 4));
                    }
                    foreach (var camera in new[] { (0, 0), (-49, -37), (107, 63), (710, 490) })
                    {
                        type.GetField("_fieldCam", flags)!.SetValue(field, camera);
                        foreach (var origin in new[] { (0, 0), (-19, -31), (width - 13, height - 17) })
                            Compare("DrawFieldBackdrop", origin.Item1, origin.Item2);
                    }
                }
                type.GetField("_fieldPicture", flags)!.SetValue(field, null);
                Compare("DrawFieldScene", 3, 11, 9);
            }
            type.GetField("_fieldPicture", flags)!.SetValue(field, picture);
            foreach (int cover in new[] { 0, 3, 8 })
            {
                type.GetField("_fieldPictureCover", flags)!.SetValue(field, cover);
                Compare("DrawFieldScene", -11, 7, 9);
            }
        }
        // More than 64 pieces must retain the original fallback rather than
        // truncate the descriptor list or overrun stack storage.
        while (pieces.Count < 65) pieces.Add((0, 0, 0, 0, p1, 73, 51, 0, 0));
        SwitchRaster.UseNative = true; Compare("DrawFieldBackdrop", 0, 0);
        SwitchRaster.UseNative = false;
        Console.WriteLine("PASS field native pixels/return equal actual original backdrop/scene: " + cases + " cases; color key, overlap order, uncovered holes, camera/parallax/flow, clipping, retained picture, PC and >64 fallback");
    }
}
