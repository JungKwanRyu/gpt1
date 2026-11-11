using System.Diagnostics;
using System.Runtime.CompilerServices;
using WogShim;

namespace WogSwitch;

// Decode progress is published only after the PCM copy. Never expose the zeroed
// tail of a Bink allocation to the mixer. State lifetime follows its PCM array.
public static class StreamedAudio
{
    private sealed class State
    {
        internal readonly long Opened = Stopwatch.GetTimestamp();
        internal int Samples, Channels, Rate, Underruns, ReportedUnderruns;
        internal bool Complete, First, Ready, Mixed, Ended, Rebuffer, Tail, Clock;
        internal double Position, LastMedia, TailReal, TailMedia;
    }
    private static readonly ConditionalWeakTable<short[], State> streams = new();
    [ThreadStatic] private static long openStart;
    public static void BeginOpen() { openStart = Stopwatch.GetTimestamp(); Host.Log("MOVIE open begin"); }
    public static void EndOpen() => Host.Log("MOVIE open ready elapsed=" + ((Stopwatch.GetTimestamp() - openStart) * 1000.0 / Stopwatch.Frequency).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms");
    public static void Begin(short[] samples) => streams.GetValue(samples, _ => new State());
    public static void Publish(short[] samples, int count)
    {
        var state = streams.GetValue(samples, _ => new State());
        lock (state) state.Samples = Math.Max(state.Samples, Math.Clamp(count, 0, samples.Length));
    }
    public static void Finish(short[] samples)
    {
        var state = streams.GetValue(samples, _ => new State());
        lock (state) state.Complete = true;
    }
    public static bool Ready(short[]? samples, int channels, int rate, bool firstFrame, bool finished)
    {
        if (samples == null) return firstFrame;
        var state = streams.GetValue(samples, _ => new State());
        lock (state)
        {
            state.Channels = channels; state.Rate = rate;
            if (finished) state.Complete = true;
            if (firstFrame && !state.First) { state.First = true; Log(state, "first-video-frame"); }
            bool ready = firstFrame && (state.Complete || (long)state.Samples >= Math.Min(samples.Length, (long)rate * channels * 3 / 4));
            if (ready && !state.Ready) { state.Ready = true; Log(state, "initial-audio-ready"); }
            return ready;
        }
    }
    private static void Log(State state, string stage)
    {
        Host.Log("MOVIE " + stage + " prepare=" + ((Stopwatch.GetTimestamp() - state.Opened) * 1000.0 / Stopwatch.Frequency).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
            "ms decoded-frames=" + (state.Samples / Math.Max(1, state.Channels)) + " audio-complete=" + state.Complete);
    }
    // The movie cannot run ahead of mixed source PCM while a decoder refills.
    // The existing output queue still supplies the usual short presentation lead.
    public static double MediaTime(double elapsed, short[]? samples, bool muted)
    {
        if (muted || samples == null || !streams.TryGetValue(samples, out var state)) return elapsed;
        lock (state)
        {
            if (state.ReportedUnderruns != state.Underruns) { state.ReportedUnderruns = state.Underruns; Log(state, "audio-refill count=" + state.Underruns); }
            if (!state.Clock) { state.Clock = true; Log(state, "playback-clock-start"); }
            if (!state.Mixed || state.Rate <= 0) return elapsed;
            double media = state.Position / state.Rate;
            if (state.Ended)
            {
                if (!state.Tail) { state.Tail = true; state.TailReal = elapsed; state.TailMedia = Math.Max(state.LastMedia, media); }
                media = state.TailMedia + Math.Max(0, elapsed - state.TailReal);
            }
            else media = Math.Min(elapsed, media);
            return state.LastMedia = Math.Max(state.LastMedia, media);
        }
    }
    internal static bool TryMix(short[] samples, int channels, double step, float left, float right,
        bool loop, ref double position, ref int laps, int[] mix, out bool alive)
    {
        alive = false;
        if (samples == null || !streams.TryGetValue(samples, out var state)) return false;
        if (channels is not (1 or 2) || mix.Length != 3528 || !double.IsFinite(step) || step <= 0 || step > 16 ||
            !double.IsFinite(position) || position < 0 || position > 1e12) throw new InvalidDataException("Invalid streaming PCM shape");
        if (!float.IsFinite(left) || !float.IsFinite(right)) left = right = 0;
        lock (state)
        {
            state.Channels = channels; state.Rate = (int)Math.Round(step * 44100);
            int available = state.Samples / channels;
            if (loop && !state.Complete) { alive = true; return true; }
            double ahead = available - position;
            if (state.Rebuffer && !state.Complete && ahead < state.Rate * .75) { alive = true; return true; }
            state.Rebuffer = false;
            int count = state.Complete ? 1764 : (int)Math.Clamp(Math.Ceiling((available - 1 - position) / step), 0, 1764);
            if (count > 0 && available > 0)
            {
                if (SwitchRaster.UseNative && Math.Abs(left) <= 8 && Math.Abs(right) <= 8)
                    SwitchMixer.MixNative(samples, available, channels, step, left, right, loop,
                        ref position, ref laps, mix, count);
                else
                    MixReference(samples, available, channels, step, left, right, loop, ref position, ref laps, mix, count);
                state.Mixed = true;
            }
            state.Position = position;
            alive = !state.Complete || position < available || (loop && available > 0);
            state.Ended = !alive;
            if (count < 1764 && !state.Complete) { state.Rebuffer = true; state.Underruns++; }
            return true;
        }
    }
    private static void MixReference(short[] samples, int frames, int channels, double step, float left, float right,
        bool loop, ref double position, ref int laps, int[] mix, int count)
    {
        for (int i = 0; i < count; i++)
        {
            long at = (long)position;
            if (at >= frames) { if (!loop) return; position -= frames * Math.Floor(position / frames); laps++; at = (long)position; }
            long next = at + 1 < frames ? at + 1 : loop ? 0 : at;
            float f = (float)(position - at);
            float l = samples[at * channels] + (samples[next * channels] - samples[at * channels]) * f;
            float r = channels == 1 ? l : samples[at * channels + 1] + (samples[next * channels + 1] - samples[at * channels + 1]) * f;
            mix[2*i] += (int)(l * left); mix[2*i+1] += (int)(r * right); position += step;
        }
    }
}
