using System.Reflection;
using WogSwitch;

internal static class MixerKernelSmoke
{
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "WarOfGenesis.Assets");
        var mixer = assembly.GetType("WarOfGenesis.Assets.AudioMixer", true)!;
        var voice = mixer.GetNestedType("Voice", BindingFlags.NonPublic)!;
        var soundType = assembly.GetType("WarOfGenesis.Assets.PcmSound", true)!;
        var original = mixer.GetMethod("SwitchOriginalMix", flags)!;
        var actual = mixer.GetMethod("Mix", flags)!;
        int cases = 0;
        foreach (bool native in new[] { false, true })
        foreach (int channels in new[] { 1, 2 })
        foreach (int rate in new[] { 11025, 22050, 32000, 44100, 48000, 96000 })
        foreach (int frames in new[] { 1, 37, 4000 })
        foreach (bool loop in new[] { false, true })
        foreach (float gain in new[] { 0f, .17f, 1f, -1.5f, float.NaN, 20f })
        {
            SwitchRaster.UseNative = native;
            short[] pcm = Enumerable.Range(0, frames * channels).Select(i => unchecked((short)(i * 17339 + 12345))).ToArray();
            object sound = Activator.CreateInstance(soundType, new object[] { pcm, rate, channels })!;
            object MakeVoice() => Activator.CreateInstance(voice, flags, null, new object[] { sound, gain, loop, 17, .63f, -.41f, false }, null)!;
            object a = MakeVoice(), b = MakeVoice();
            var position = voice.GetField("Position", flags)!;
            var laps = voice.GetField("Laps", flags)!;
            double start = frames > 1 ? frames - .375 : 0;
            position.SetValue(a, start); position.SetValue(b, start);
            laps.SetValue(a, 3); laps.SetValue(b, 3);
            for (int chunk = 0; chunk < 3; chunk++)
            {
                int[] expected = Enumerable.Range(0, 3528).Select(i => unchecked(i * 7712345)).ToArray();
                int[] result = (int[])expected.Clone();
                var alive = original.Invoke(null, new object[] { a, expected, .7f });
                var handled = actual.Invoke(null, new object[] { b, result, .7f });
                if (!Equals(alive, handled) || !expected.SequenceEqual(result) ||
                    !Equals(position.GetValue(a), position.GetValue(b)) || !Equals(laps.GetValue(a), laps.GetValue(b)))
                    throw new InvalidDataException($"Native mixer differs from actual original: native={native} ch={channels} rate={rate} frames={frames} loop={loop} gain={gain} chunk={chunk}");
                cases++;
            }
        }
        SwitchRaster.UseNative = false;
        Console.WriteLine("PASS actual original versus native audio voice PCM, position, laps and end state: " + cases + " cases; mono/stereo, six sample rates, interpolation, pan, gain, looping, overflow, nonfinite/PC fallback");
    }
}
