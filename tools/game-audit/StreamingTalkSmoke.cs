using System.Reflection;
using System.Runtime.CompilerServices;
using WogSwitch;

internal static class StreamingTalkSmoke
{
    internal static void Run(Assembly game, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var assets = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "WarOfGenesis.Assets");
        var soundType = assets.GetType("WarOfGenesis.Assets.PcmSound", true)!;
        var mixerType = assets.GetType("WarOfGenesis.Assets.AudioMixer", true)!;
        var voiceType = mixerType.GetNestedType("Voice", BindingFlags.NonPublic)!;
        var original = mixerType.GetMethod("SwitchOriginalMix", flags)!;
        foreach (var item in new[] { (assets.GetType("WarOfGenesis.Assets.BinkPlayback", true)!, "AudioLoop"),
            (assets.GetType("WarOfGenesis.Assets.BinkPlayback", true)!, "Start"), (game.GetType("DuelDx.CutscenePlayer", true)!, "Update"),
            (game.GetType("DuelDx.TalkBox", true)!, "AdvanceTalk") })
        {
            Console.WriteLine("JIT PREPARE " + item.Item1.FullName + "." + item.Item2);
            using var image = Mono.Cecil.AssemblyDefinition.ReadAssembly(item.Item1.Assembly.Location);
            var code = image.MainModule.GetType(item.Item1.FullName!).Methods.Single(m => m.Name == item.Item2);
            File.AppendAllLines(Path.Combine(output, "playback-methods-il.txt"), new[] { code.FullName }.Concat(code.Body.Instructions.Select(i => i.ToString())));
            RuntimeHelpers.PrepareMethod(item.Item1.GetMethod(item.Item2, flags)!.MethodHandle);
        }
        foreach (string method in new[] { "DrawTalkWindow", "DrawTalkLines", "DrawUiScaled", "DrawTalkFace" })
            RuntimeHelpers.PrepareMethod(game.GetType("DuelDx.TalkBox", true)!.GetMethod(method, flags)!.MethodHandle);
        Console.WriteLine("PASS actual patched movie/update/audio-loop and dialogue methods pass runtime JIT validation");
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
        if ((int)tw.GetField("Revealed", flags)!.GetValue(text)! != 0 || (int)tw.GetField("VoiceLoaded", flags)!.GetValue(text)! != 0)
            throw new InvalidDataException("Pending voice revealed text ahead of PCM or voice state was altered");
        // Voice-paced reveal uses the local clock, while a long absence is excluded.
        object fakeMixer = RuntimeHelpers.GetUninitializedObject(mixerType);
        mixerType.GetField("_gate", flags)!.SetValue(fakeMixer, new object());
        var effectsField = mixerType.GetField("_effects", flags)!;
        var effects = (System.Collections.IList)Activator.CreateInstance(effectsField.FieldType)!;
        object voiceSound = Activator.CreateInstance(soundType, new object[] { new short[44100], 44100, 1 })!;
        effects.Add(Activator.CreateInstance(voiceType, flags, null, new object[] { voiceSound, 1f, false, 123, 1f, 1f, false }, null)!);
        effectsField.SetValue(fakeMixer, effects); wt.GetField("_mixer", flags)!.SetValue(window, fakeMixer);
        var spoken = Activator.CreateInstance(tw, true)!;
        foreach (var pair in new (string, object)[] { ("Clean", (object)"가나다라마바사아자차카타파하"), ("CharLine", new int[14]), ("MaxLines", 3),
            ("Voice", 123), ("VoiceTag", 123), ("VoiceLoaded", 1), ("VoiceStarted", true), ("VoiceSeconds", 2f) })
            tw.GetField(pair.Item1, flags)!.SetValue(spoken, pair.Item2);
        var advance = tt.GetMethod("AdvanceTalk", flags)!;
        advance.Invoke(talk, new object[] { spoken, 0 });
        wt.GetField("_lastTime", flags)!.SetValue(window, .1); wt.GetField("_realTime", flags)!.SetValue(window, .3);
        advance.Invoke(talk, new object[] { spoken, 3 });
        int shown = (int)tw.GetField("Revealed", flags)!.GetValue(spoken)!;
        if (shown < 2) throw new InvalidDataException("Actual voiced reveal still follows capped game time");
        wt.GetField("_lastTime", flags)!.SetValue(window, .2); wt.GetField("_realTime", flags)!.SetValue(window, 100.0);
        advance.Invoke(talk, new object[] { spoken, 6 });
        if ((int)tw.GetField("Revealed", flags)!.GetValue(spoken)! > shown + 1) throw new InvalidDataException("Actual voiced text jumped after a long absence");
        // Queued tail audio remains the text clock even after the mixer voice ends.
        VoicePlayback.Opening(0);int outputClock=VoicePlayback.StartOutput(44100);
        VoicePlayback.BeginBlock(0);VoicePlayback.Record(123,0,1764,1);
        var synced=Activator.CreateInstance(tw,true)!;
        foreach(var pair in new (string,object)[]{("Clean",new string('가',100)),("CharLine",new int[100]),("MaxLines",3),
            ("Voice",123),("VoiceTag",123),("VoiceLoaded",1),("VoiceStarted",true),("VoiceSeconds",1f)})tw.GetField(pair.Item1,flags)!.SetValue(synced,pair.Item2);
        wt.GetField("_lastTime",flags)!.SetValue(window,10.0);wt.GetField("_realTime",flags)!.SetValue(window,500.0);
        advance.Invoke(talk,new object[]{synced,0});
        VoicePlayback.Presented(outputClock,882);
        wt.GetField("_lastTime",flags)!.SetValue(window,10.1);wt.GetField("_realTime",flags)!.SetValue(window,500.4);
        advance.Invoke(talk,new object[]{synced,12});
        if((int)tw.GetField("Revealed",flags)!.GetValue(synced)!=2)throw new InvalidDataException("Actual dialogue did not follow device PCM position");
        VoicePlayback.BeginBlock(1);VoicePlayback.Record(123,42336,44100,1);effects.Clear();
        advance.Invoke(talk,new object[]{synced,15});
        if((int)tw.GetField("Revealed",flags)!.GetValue(synced)!=2)throw new InvalidDataException("Text finished before queued voice tail reached device");
        VoicePlayback.Presented(outputClock,3528);advance.Invoke(talk,new object[]{synced,18});
        if((int)tw.GetField("Revealed",flags)!.GetValue(synced)!=100)throw new InvalidDataException("Text failed to finish after final PCM was played");
        VoicePlayback.StopOutput(outputClock);
        Console.WriteLine("PASS actual voiced text follows device PCM and waits for queued tail after mixer voice ends");
        var ct = game.GetType("DuelDx.CutscenePlayer", true)!;
        var cut = Activator.CreateInstance(ct, flags, null, new[] { window }, null)!;
        string moviePath = Path.GetTempFileName();
        try
        {
            var file = new FileStream(moviePath, FileMode.Open, FileAccess.Read);
            var wgm = ct.GetNestedType("WgmClip", BindingFlags.NonPublic)!;
            var clip = Activator.CreateInstance(wgm, flags, null, new object?[] { file, 640, 480, 15.0, new long[] { 0 }, null }, null)!;
            ct.GetField("_clip", flags)!.SetValue(cut, clip); ct.GetField("_soundTag", flags)!.SetValue(cut, 123);
            ((System.Collections.Generic.Queue<int>)ct.GetField("_queue", flags)!.GetValue(cut)!).Enqueue(8);
            var handle = file.SafeFileHandle;
            ct.GetMethod("Skip", flags)!.Invoke(cut, null);
            if (!Equals(ct.GetProperty("Playing", flags)!.GetValue(cut), false) || effects.Count != 0 || !handle.IsClosed)
                throw new InvalidDataException("Skip did not stop voice, release clip and clear queued playback");
        }
        finally { File.Delete(moviePath); }
        Console.WriteLine("PASS actual voiced text uses short-frame recovery and excludes long absence; movie Skip stops voice, closes clip and clears queue");
        // Existing field-specific layer/wave/choice renderer must remain in charge.
        var ft = game.GetType("DuelDx.FieldScene", true)!; var field = RuntimeHelpers.GetUninitializedObject(ft);
        ft.GetFields(flags).Single(f => f.FieldType == wt).SetValue(field, window);
        var fieldData = ft.GetField("_field", flags)!; fieldData.SetValue(field, RuntimeHelpers.GetUninitializedObject(fieldData.FieldType));
        wt.GetField("_fldScene", flags)!.SetValue(window, field);
        wt.GetMethod("DrawBattleTalk", flags)!.Invoke(window, new object[] { true }); // Tlk is uninitialized: duplicate would throw.
        SwitchRaster.UseNative = false;
        Console.WriteLine("PASS streaming PCM original-equivalence, prebuffer, unpublished-tail guard, underrun/refill, monotonic movie clock, mute/end/cancel: " + cases + " cases");
        Console.WriteLine("PASS actual pending-voice talk waits for synchronized PCM; field suppresses duplicate battle talk; short-frame text recovery excludes suspend and pause");
    }
}
