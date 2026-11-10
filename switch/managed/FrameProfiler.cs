using System.Diagnostics;

namespace WogSwitch;

public static class FrameProfiler
{
    private static readonly long[] starts = new long[11], ticks = new long[11], counts = new long[11];
    private static readonly string[] names = { "compose", "upload", "draw", "pump", "background", "battle", "field", "moses", "ui", "title", "cutscene" };
    private static Action<string>? logger;
    private static string? report;
    private static long since, frames, lastFrame, maxInterval;
    private static bool warmup;

    internal static void Reset(string root, Action<string> log)
    {
        Array.Clear(starts); Array.Clear(ticks); Array.Clear(counts);
        warmup = true; frames = lastFrame = maxInterval = 0; since = Stopwatch.GetTimestamp();
        logger = log; report = Path.Combine(root, "performance.log");
        File.WriteAllText(report, "WogSwitch 0.5.11 frame wall times; draw includes swap; not GPU-only time; gaps over 60s recorded separately\n");
    }

    public static void Begin(int phase) => BeginAt(phase, Stopwatch.GetTimestamp());
    internal static void BeginAt(int phase, long now) => starts[phase] = now;
    public static void End(int phase) => EndAt(phase, Stopwatch.GetTimestamp());
    internal static void EndAt(int phase, long now)
    {
        long duration = now - starts[phase];
        if (duration > Stopwatch.Frequency * 60)
        {
            string line = "PERF LONG_GAP phase=" + names[phase] + " seconds=" +
                (duration / (double)Stopwatch.Frequency).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                "; suspend or long stall; interval excluded from regular FPS";
            try { File.AppendAllText(report!, line + "\n"); logger?.Invoke(line); } catch { }
            Array.Clear(ticks); Array.Clear(counts); frames = lastFrame = maxInterval = 0; since = now; warmup = true;
            return;
        }
        ticks[phase] += duration; counts[phase]++;
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
        for (int i = 0; i < 4; i++)
            line += " " + names[i] + "=" + (ticks[i] * 1000.0 / Stopwatch.Frequency / Math.Max(1, counts[i])).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms";
        // Section timings are totals per compose, not per function call. These
        // top-level Compose calls do not overlap; uninstrumented work is rest.
        long sections = 0;
        for (int i = 4; i < names.Length; i++)
        {
            sections += ticks[i];
            line += " " + names[i] + "=" + (ticks[i] * 1000.0 / Stopwatch.Frequency / Math.Max(1, counts[0])).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms";
        }
        line += " rest=" + (Math.Max(0, ticks[0] - sections) * 1000.0 / Stopwatch.Frequency / Math.Max(1, counts[0])).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms";
        line += " max-frame=" + (maxInterval * 1000.0 / Stopwatch.Frequency).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms";
        try { File.AppendAllText(report, line + "\n"); logger?.Invoke(line); }
        catch { /* Timing must not terminate the game. */ }
        Array.Clear(ticks); Array.Clear(counts); frames = maxInterval = 0; since = Stopwatch.GetTimestamp();
    }
}
