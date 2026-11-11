using System.Runtime.InteropServices;

namespace WogSwitch;

// Keeps voice state and queue/clock policy in the original game mixer.
public static class SwitchMixer
{
    public static unsafe bool TryMix(short[] samples, int channels, double step, float left, float right,
        bool loop, ref double position, ref int laps, int[] mix, out bool alive)
    {
        if (StreamedAudio.TryMix(samples, channels, step, left, right, loop, ref position, ref laps, mix, out alive)) return true;
        alive = false;
        if (!SwitchRaster.UseNative || samples == null || mix == null || channels is not (1 or 2) ||
            samples.Length == 0 || samples.Length % channels != 0 || mix.Length != 3528 ||
            !double.IsFinite(step) || step <= 0 || step > 16 || !double.IsFinite(position) || position < 0 || position > 1e12)
            return false;
        if (!float.IsFinite(left) || !float.IsFinite(right)) left = right = 0;
        if (Math.Abs(left) > 8 || Math.Abs(right) > 8) return false;
        fixed (short* source = samples)
        fixed (int* destination = mix)
            alive = wog_mix_voice(source, samples.Length / channels, channels, step, left, right,
                loop ? 1 : 0, ref position, ref laps, destination, mix.Length / 2) != 0;
        return true;
    }

    internal static unsafe void MixNative(short[] samples, int frames, int channels, double step, float left, float right,
        bool loop, ref double position, ref int laps, int[] mix, int count)
    {
        fixed (short* source = samples)
        fixed (int* destination = mix)
            wog_mix_voice(source, frames, channels, step, left, right, loop ? 1 : 0, ref position, ref laps, destination, count);
    }

    [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int wog_mix_voice(short* samples, int frames, int channels, double step,
        float left, float right, int loop, ref double position, ref int laps, int* mix, int outputFrames);
}
