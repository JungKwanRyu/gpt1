using System.Diagnostics;
namespace WogSwitch;
public static class DetailProfiler
{
    private static readonly string[] names = { "talk-window", "talk-lines", "talk-scale", "talk-face-row", "field-layers", "field-backdrop" };
    private static readonly long[] starts = new long[6], totals = new long[6], calls = new long[6], maximum = new long[6];
    public static void Begin(int phase) => starts[phase] = Stopwatch.GetTimestamp();
    public static void End(int phase)
    {
        long ticks = Stopwatch.GetTimestamp() - starts[phase];
        if (ticks > Stopwatch.Frequency * 60) return;
        totals[phase] += ticks; calls[phase]++; maximum[phase] = Math.Max(maximum[phase], ticks);
    }
    internal static void Reset() { Array.Clear(totals); Array.Clear(calls); Array.Clear(maximum); }
    internal static void Flush(Action<string>? log)
    {
        for (int i = 0; i < names.Length; i++)
            if (calls[i] > 0) log?.Invoke("DETAIL " + names[i] + " calls=" + calls[i] + " average=" +
                (totals[i] * 1000.0 / Stopwatch.Frequency / calls[i]).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                "ms max=" + (maximum[i] * 1000.0 / Stopwatch.Frequency).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "ms; nested timings are not additive");
        Reset();
    }
}
