using System.Reflection;
using System.Text;

internal static class ShaderBlobSmoke
{
    internal static void Run()
    {
        var type = typeof(WogShim.Host).Assembly.GetType("WogShim.Gpu.HlslBlob", true)!;
        var pack = type.GetMethod("Pack", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        foreach (var values in new string?[][] {
            new[] { "한글 shader\nvoid main() {}", "entry", "ps_5_0", "title.hlsl" },
            new string?[] { "", null, "vs_5_0", "" } })
        {
            var actual = (byte[])pack.Invoke(null, values)!;
            var expected = Encoding.UTF8.GetBytes(string.Join("\0", new[] { "WOGHLSL", values[3], values[1], values[2], values[0] }));
            if (!actual.SequenceEqual(expected)) throw new InvalidDataException("SDK9 shader blob byte layout changed");
        }
        Console.WriteLine("PASS actual adapted shader blob preserves five UTF8 fields, order, nulls and NUL separators without BCL10 InlineArray5");
    }
}
