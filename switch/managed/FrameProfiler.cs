using System.Diagnostics;

namespace WogSwitch;

public static class FrameProfiler
{
    private static readonly long[] starts = new long[4], ticks = new long[4], counts = new long[4];
    private static readonly string[] names = { "compose", "upload", "draw", "pump" };
    private static Action<string>? logger;
    private static string? report;
    private static long since, frames, lastFrame, maxInterval;
    private static bool warmup;

    internal static void Reset(string root, Action<string> log)
    {
        Array.Clear(starts); Array.Clear(ticks); Array.Clear(counts);
        warmup = true; frames = lastFrame = maxInterval = 0; since = Stopwatch.GetTimestamp();
        logger = log; report = Path.Combine(root, "performance.log");
        File.WriteAllText(report, "WogSwitch 0.5.9 frame wall times; draw includes swap; not GPU-only time\n");
    }

    public static void Begin(int phase) => starts[phase] = Stopwatch.GetTimestamp();
    public static void End(int phase)
    {
        long now = Stopwatch.GetTimestamp();
        ticks[phase] += now - starts[phase]; counts[phase]++;
        if (phase == 2 && warmup && lastFrame != 0)
        {
            // Original-data loading and the first shader/font warmup are not
            // steady-state menu FPS. Begin the first sample after frame one.
            Array.Clear(ticks); Array.Clear(counts); frames = maxInterval = 0; since = now; warmup = false;
        }
        else if (phase == 2 && !warmup && now - since >= Stopwatch.Frequency * 5) Flush();
    }

    internal static void Presented()
    {
        long now = Stopwatch.GetTimestamp(); frames++;
        if (lastFrame != 0) maxInterval = Math.Max(maxInterval, now - lastFrame);
        lastFrame = now;
    }

    internal static void Flush()
    {
        if (report == null) return;
        long now = Stopwatch.GetTimestamp();
        double seconds = (now - since) / (double)Stopwatch.Frequency;
        string line = "PERF frames=" + frames + " fps=" + (frames / Math.Max(seconds, .001)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < names.Length; i++)
            line += " " + names[i] + "=" + (ticks[i] * 1000.0 / Stopwatch.Frequency / Math.Max(1, counts[i])).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms";
        line += " max-frame=" + (maxInterval * 1000.0 / Stopwatch.Frequency).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms";
        try { File.AppendAllText(report, line + "\n"); logger?.Invoke(line); }
        catch { /* Timing must not terminate the game. */ }
        Array.Clear(ticks); Array.Clear(counts); frames = maxInterval = 0; since = Stopwatch.GetTimestamp();
    }
}
