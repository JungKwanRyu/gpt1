using System.Reflection;
using System.Runtime.CompilerServices;
using WogSwitch;

internal static class StreamingTalkSmoke
{
    internal static void Run(Assembly game)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var assets = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "WarOfGenesis.Assets");
        var soundType = assets.GetType("WarOfGenesis.Assets.PcmSound", true)!;
        var mixerType = assets.GetType("WarOfGenesis.Assets.AudioMixer", true)!;
        var voiceType = mixerType.GetNestedType("Voice", BindingFlags.NonPublic)!;
        var original = mixerType.GetMethod("SwitchOriginalMix", flags)!;
        int cases = 0;
        foreach (bool native in new[] { false, true })
        foreach (int channels in new[] { 1, 2 })
        foreach (int rate in new[] { 11025, 22050, 44100, 48000 })
        {
            SwitchRaster.UseNative = native;
            short[] pcm = Enumerable.Range(0, rate * channels * 2).Select(i => unchecked((short)(i * 17339 + 12345))).ToArray();
            StreamedAudio.Begin(pcm);
            if (StreamedAudio.Ready(pcm, channels, rate, true, false)) throw new InvalidDataException("Unpublished PCM was ready");
            StreamedAudio.Publish(pcm, rate * channels / 2);
            if (StreamedAudio.Ready(pcm, channels, rate, true, false)) throw new InvalidDataException("Insufficient prebuffer was ready");
            StreamedAudio.Publish(pcm, rate * channels);
            if (!StreamedAudio.Ready(pcm, channels, rate, true, false)) throw new InvalidDataException("Prebuffer did not release playback before full audio decode");
            object sound = Activator.CreateInstance(soundType, new object[] { pcm, rate, channels })!;
            object voice = Activator.CreateInstance(voiceType, flags, null, new object[] { sound, .7f, false, 17, .63f, -.41f, false }, null)!;
            var position = voiceType.GetField("Position", flags)!;
            double pos = 0, step = rate / 44100.0; int laps = 0;
            int[] result = new int[3528], expected = new int[3528];
            // Full initial chunks must equal actual original interpolation exactly.
            for (int i = 0; i < 8; i++)
            {
                Array.Clear(result); Array.Clear(expected);
                original.Invoke(null, new object[] { voice, expected, 1f });
                if (!SwitchMixer.TryMix(pcm, channels, step, .7f * .63f, .7f * -.41f, false, ref pos, ref laps, result, out bool alive) || !alive || !result.SequenceEqual(expected) || !Equals(position.GetValue(voice), pos))
                    throw new InvalidDataException("Streaming prebuffer differs from actual original PCM");
                cases++;
            }
            // Exhaust the published prefix, including fractional interpolation.
            while (pos < rate - 1 - step * 1764)
                SwitchMixer.TryMix(pcm, channels, step, 1, 1, false, ref pos, ref laps, result, out _);
            Array.Clear(result);
            double before = pos;
            SwitchMixer.TryMix(pcm, channels, step, 1, 1, false, ref pos, ref laps, result, out bool pending);
            int valid = (int)Math.Clamp(Math.Ceiling((rate - 1 - before) / step), 0, 1764);
            if (!pending || pos > rate - 1 + step || result.Skip(2 * valid).Any(x => x != 0)) throw new InvalidDataException("Unpublished tail leaked into streaming output");
            Array.Clear(result); before = pos;
            SwitchMixer.TryMix(pcm, channels, step, 1, 1, false, ref pos, ref laps, result, out pending);
            if (!pending || pos != before || result.Any(x => x != 0)) throw new InvalidDataException("Decoder underrun advanced voice or read unwritten PCM");
            double frozen = StreamedAudio.MediaTime(10, pcm, false);
            if (StreamedAudio.MediaTime(20, pcm, false) != frozen || StreamedAudio.MediaTime(20, pcm, true) != 20) throw new InvalidDataException("Movie clock did not freeze on refill or muted path changed");
            StreamedAudio.Publish(pcm, pcm.Length); StreamedAudio.Finish(pcm);
            position.SetValue(voice, pos); Array.Clear(result); Array.Clear(expected);
            original.Invoke(null, new object[] { voice, expected, 1f });
            SwitchMixer.TryMix(pcm, channels, step, .7f * .63f, .7f * -.41f, false, ref pos, ref laps, result, out pending);
            if (!result.SequenceEqual(expected) || !Equals(position.GetValue(voice), pos)) throw new InvalidDataException("Decoder refill lost or repeated PCM samples");
            if (StreamedAudio.MediaTime(21, pcm, false) < frozen) throw new InvalidDataException("Movie media clock moved backwards");
            while (pending) SwitchMixer.TryMix(pcm, channels, step, 1, 1, false, ref pos, ref laps, result, out pending);
            double tail = StreamedAudio.MediaTime(30, pcm, false);
            if (Math.Abs(StreamedAudio.MediaTime(31, pcm, false) - tail - 1) > 1e-8) throw new InvalidDataException("Movie froze after a short audio track ended");
            cases += 6;
        }
        short[] failed = new short[100]; StreamedAudio.Begin(failed); StreamedAudio.Finish(failed);
        double emptyPos = 0; int emptyLaps = 0;
        SwitchMixer.TryMix(failed, 1, 1, 1, 1, false, ref emptyPos, ref emptyLaps, new int[3528], out bool emptyAlive);
        if (emptyAlive || StreamedAudio.MediaTime(12, failed, false) != 12) throw new InvalidDataException("Cancelled/failed empty decoder did not terminate safely");
        var key = new object();
        if (DialogueClock.Tick(key, 0, 0, 0, 100) != 0 || DialogueClock.Tick(key, 3, .1, .3, 100) != 9) throw new InvalidDataException("Text clock did not recover short-frame loss");
        int resumed = DialogueClock.Tick(key, 6, .2, 100, 100);
        if (resumed != 12 || DialogueClock.Tick(key, 6, .2, 101, 0) != 12) throw new InvalidDataException("Suspend/pause caused a text catch-up jump");
        // Execute the actual patched AdvanceTalk while voice preparation is pending.
        var wt = game.GetType("DuelDx.GameWindow", true)!; var window = RuntimeHelpers.GetUninitializedObject(wt);
        var tt = game.GetType("DuelDx.TalkBox", true)!; var talk = RuntimeHelpers.GetUninitializedObject(tt);
        tt.GetFields(flags).Single(f => f.FieldType == wt).SetValue(talk, window);
        var tw = tt.GetNestedType("TalkWindow", flags)!; var text = Activator.CreateInstance(tw, true)!;
        tw.GetField("Clean", flags)!.SetValue(text, "가나다라마바사아자차카타파하");
        tw.GetField("CharLine", flags)!.SetValue(text, new int[14]); tw.GetField("MaxLines", flags)!.SetValue(text, 3);
        tw.GetField("Voice", flags)!.SetValue(text, 123); tw.GetField("VoiceStarted", flags)!.SetValue(text, true);
        wt.GetField("_gameSpeed", flags)!.SetValue(window, 100);
        tt.GetMethod("AdvanceTalk", flags)!.Invoke(talk, new object[] { text, 12 });
        if ((int)tw.GetField("Revealed", flags)!.GetValue(text)! == 0 || (int)tw.GetField("VoiceLoaded", flags)!.GetValue(text)! != 0)
            throw new InvalidDataException("Voice preparation still blocks first text or voice state was altered");
        // Existing field-specific layer/wave/choice renderer must remain in charge.
        var ft = game.GetType("DuelDx.FieldScene", true)!; var field = RuntimeHelpers.GetUninitializedObject(ft);
        ft.GetFields(flags).Single(f => f.FieldType == wt).SetValue(field, window);
        var fieldData = ft.GetField("_field", flags)!; fieldData.SetValue(field, RuntimeHelpers.GetUninitializedObject(fieldData.FieldType));
        wt.GetField("_fldScene", flags)!.SetValue(window, field);
        wt.GetMethod("DrawBattleTalk", flags)!.Invoke(window, new object[] { true }); // Tlk is uninitialized: duplicate would throw.
        SwitchRaster.UseNative = false;
        Console.WriteLine("PASS streaming PCM original-equivalence, prebuffer, unpublished-tail guard, underrun/refill, monotonic movie clock, mute/end/cancel: " + cases + " cases");
        Console.WriteLine("PASS actual pending-voice talk reveals Korean text; field suppresses duplicate battle talk; short-frame text recovery excludes suspend and pause");
    }
}
