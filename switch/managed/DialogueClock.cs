using System.Diagnostics;
using System.Runtime.CompilerServices;
using WogShim;
namespace WogSwitch;

// Only text reveal gets a local clock; battle simulation, input and auto-close
// keep the game's existing clock. Long absences do not become catch-up text.
public static class DialogueClock
{
    private sealed class State
    {
        internal bool Initialized, Waiting, SeenText;
        internal double Game, Real, Offset;
        internal long WaitStart;
    }
    private static readonly ConditionalWeakTable<object, State> states = new();
    private static State Get(object window, double game, double real, int speed)
    {
        var state = states.GetValue(window, _ => new State());
        if (state.Initialized)
        {
            double dg = game - state.Game, dr = real - state.Real;
            if (dg > 0 && dr > 0 && dr <= 1 && speed > 0)
                state.Offset += Math.Max(0, dr * speed / 100.0 - dg);
        }
        state.Initialized = true; state.Game = game; state.Real = real;
        return state;
    }
    public static int Tick(object window, int tick, double game, double real, int speed)
    {
        var state = Get(window, game, real, speed);
        return (int)Math.Clamp(tick + state.Offset * 30, int.MinValue, int.MaxValue);
    }
    public static double Now(object window, double game, double real, int speed) => game + Get(window, game, real, speed).Offset;
    public static void Observe(object window, int voice, int loaded, int revealed)
    {
        var state = states.GetValue(window, _ => new State());
        if (voice > 0 && loaded == 0 && !state.Waiting)
        {
            state.Waiting = true; state.WaitStart = Stopwatch.GetTimestamp(); Host.Log("TALK voice-load begin; text may reveal while audio loads");
        }
        if (state.Waiting && loaded != 0)
        {
            state.Waiting = false;
            Host.Log("TALK voice-load end status=" + loaded + " elapsed=" + ((Stopwatch.GetTimestamp() - state.WaitStart) * 1000.0 / Stopwatch.Frequency).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms");
        }
        if (revealed > 0 && !state.SeenText) { state.SeenText = true; Host.Log("TALK first-text-observed; audio-status=" + loaded); }
    }
}
