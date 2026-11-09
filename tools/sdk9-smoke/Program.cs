using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

if (Environment.Version.Major != 9 || args.Length != 2) throw new InvalidOperationException("Run this smoke on .NET 9 with the game-audit output path");
var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
var pack = Assembly.LoadFrom(Path.GetFullPath(args[1])).GetType("WogShim.Gpu.HlslBlob", true)!.GetMethod("Pack", flags)!;
string?[][] cases = { new[] { "한글 shader\nvoid main() {}", "entry", "ps_5_0", "title.hlsl" }, new string?[] { "", null, "vs_5_0", "" } };
foreach (var values in cases)
{
    byte[] actual = (byte[])pack.Invoke(null, values)!;
    byte[] expected = Encoding.UTF8.GetBytes(string.Join("\0", new[] { "WOGHLSL", values[3], values[1], values[2], values[0] }));
    if (!actual.SequenceEqual(expected)) throw new InvalidDataException("Shader blob differs on .NET 9");
}
Console.WriteLine("PASS .NET 9 actual adapted shader blob loads and preserves UTF8/NUL bytes");
string file = Directory.EnumerateFiles(Path.Combine(args[0], "app/patched-load"), "WarOfGenesis.dll", SearchOption.AllDirectories)
    .First(p => !p.Contains(".incomplete-") && File.Exists(Path.Combine(Path.GetDirectoryName(p)!, "complete.sha256")));
var game = Assembly.LoadFrom(Path.GetFullPath(file));
var buffer = game.GetType("WogSwitch.Compatibility.InlineArray4`1", true)!;
var check = typeof(LayoutCheck).GetMethod(nameof(LayoutCheck.Check))!;
foreach (var item in new[] { (typeof(byte), 4), (typeof(int), 16), (typeof(long), 32), (typeof(object), 4 * IntPtr.Size) })
    check.MakeGenericMethod(buffer.MakeGenericType(item.Item1)).Invoke(null, new object[] { item.Item2 });
Console.WriteLine("PASS .NET 9 actual patched InlineArray4<byte/int/long/object> layout and final-element access; CoreCLR smoke does not prove Switch GPU/gameplay");

internal static class LayoutCheck
{
    public static void Check<T>(int expected) where T : struct
    {
        if (Unsafe.SizeOf<T>() != expected) throw new InvalidDataException("Inline array size mismatch: " + typeof(T));
        // Do not write raw bytes into a reference-containing generic struct.
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) return;
        T value = default;
        ref byte first = ref Unsafe.As<T, byte>(ref value);
        Unsafe.Add(ref first, expected - 1) = 0x5a;
        if (Unsafe.Add(ref first, expected - 1) != 0x5a) throw new InvalidDataException("Last inline array byte inaccessible");
    }
}
