using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;

internal static class StreamingPlaybackPatches
{
    internal static void ApplyAssets(ModuleDefinition module, Action<string> log)
    {
        var type = module.GetType("WarOfGenesis.Assets.BinkPlayback");
        var loop = type.Methods.Single(m => m.Name == "AudioLoop");
        var code = loop.Body.Instructions;
        VariableDefinition Local(Instruction i) => i.OpCode.Code switch {
            Code.Ldloc_0 or Code.Stloc_0 => loop.Body.Variables[0], Code.Ldloc_1 or Code.Stloc_1 => loop.Body.Variables[1],
            Code.Ldloc_2 or Code.Stloc_2 => loop.Body.Variables[2], Code.Ldloc_3 or Code.Stloc_3 => loop.Body.Variables[3],
            Code.Ldloc or Code.Ldloc_S or Code.Stloc or Code.Stloc_S => (VariableDefinition)i.Operand,
            _ => throw new InvalidDataException("Bink PCM local changed") };
        var min = code.Single(i => i.Operand is MethodReference m && m.Name == "Min" && m.DeclaringType.FullName == "System.Math");
        var count = Local(min.Next);
        var increment = code.Single(i => i.OpCode == OpCodes.Add && i.Previous.OpCode.Code.ToString().StartsWith("Ldloc") && Local(i.Previous) == count);
        var at = Local(increment.Next);
        var il = loop.Body.GetILProcessor(); var after = increment.Next.Next;
        il.InsertBefore(after, il.Create(OpCodes.Ldarg_1)); il.InsertBefore(after, il.Create(OpCodes.Ldloc, at));
        il.InsertBefore(after, il.Create(OpCodes.Call, module.ImportReference(typeof(StreamedAudio).GetMethod(nameof(StreamedAudio.Publish))!)));
        var finished = code.Single(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "_audioDone");
        // Stay in the decoder's existing finally block: cancellation/failure also publishes completion.
        il.InsertAfter(finished, il.Create(OpCodes.Call, module.ImportReference(typeof(StreamedAudio).GetMethod(nameof(StreamedAudio.Finish))!)));
        il.InsertAfter(finished, il.Create(OpCodes.Ldarg_1));
        loop.Body.MaxStackSize += 2;
        PlatformPatches.ExpandShortBranches(loop);
        var sound = type.Methods.Single(m => m.Name == "get_Sound");
        var soundType = sound.ReturnType.Resolve();
        var samples = soundType.Methods.Single(m => m.Name == "get_Samples");
        foreach (var ctor in type.Methods.Where(m => m.IsConstructor && !m.IsStatic))
        {
            var writer = ctor.Body.GetILProcessor();
            foreach (var assignment in ctor.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "<Sound>k__BackingField").ToArray())
            {
                var next = assignment.Next;
                writer.InsertBefore(next, writer.Create(OpCodes.Ldarg_0)); writer.InsertBefore(next, writer.Create(OpCodes.Call, sound));
                writer.InsertBefore(next, writer.Create(OpCodes.Callvirt, samples));
                writer.InsertBefore(next, writer.Create(OpCodes.Call, module.ImportReference(typeof(StreamedAudio).GetMethod(nameof(StreamedAudio.Begin))!)));
            }
            PlatformPatches.ExpandShortBranches(ctor);
        }
        var start = type.Methods.Single(m => m.Name == "Start" && m.IsStatic);
        var trace = start.Body.GetILProcessor(); var entry = start.Body.Instructions[0];
        trace.InsertBefore(entry, trace.Create(OpCodes.Call, module.ImportReference(typeof(StreamedAudio).GetMethod(nameof(StreamedAudio.BeginOpen))!)));
        foreach (var ret in start.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
        {
            ret.OpCode = OpCodes.Call; ret.Operand = module.ImportReference(typeof(StreamedAudio).GetMethod(nameof(StreamedAudio.EndOpen))!);
            trace.InsertAfter(ret, trace.Create(OpCodes.Ret));
        }
        PlatformPatches.ExpandShortBranches(start);
        log("PASS Bink PCM publishes only copied samples; decoder finally marks completion; incremental prebuffer enabled");
    }

    internal static void ApplyGame(ModuleDefinition module, Action<string> log)
    {
        var cut = module.GetType("DuelDx.CutscenePlayer");
        var clip = cut.NestedTypes.Single(t => t.Name == "Clip");
        var bink = cut.NestedTypes.Single(t => t.Name == "BinkClip");
        var getSound = clip.Methods.Single(m => m.Name == "get_Sound");
        var soundType = getSound.ReturnType;
        MethodReference Getter(string name, TypeReference result) => new(name, result, soundType) { HasThis = true };
        var getSamples = Getter("get_Samples", new ArrayType(module.TypeSystem.Int16));
        var getRate = Getter("get_SampleRate", module.TypeSystem.Int32);
        var getChannels = Getter("get_Channels", module.TypeSystem.Int32);
        var playback = bink.Fields.Single(f => f.FieldType.Name == "BinkPlayback");
        var ready = bink.Methods.Single(m => m.Name == "get_Ready");
        ready.Body = new MethodBody(ready) { InitLocals = true, MaxStackSize = 8 };
        var sound = new VariableDefinition(soundType); ready.Body.Variables.Add(sound);
        var il = ready.Body.GetILProcessor(); var noSound = il.Create(OpCodes.Ldnull); var args = il.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, getSound); il.Emit(OpCodes.Stloc, sound);
        il.Emit(OpCodes.Ldloc, sound); il.Emit(OpCodes.Brfalse, noSound);
        il.Emit(OpCodes.Ldloc, sound); il.Emit(OpCodes.Callvirt, getSamples);
        il.Emit(OpCodes.Ldloc, sound); il.Emit(OpCodes.Callvirt, getChannels);
        il.Emit(OpCodes.Ldloc, sound); il.Emit(OpCodes.Callvirt, getRate); il.Emit(OpCodes.Br, args);
        il.Append(noSound); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ldc_I4_0); il.Append(args);
        il.Emit(OpCodes.Ldfld, playback); il.Emit(OpCodes.Callvirt, new MethodReference("get_FirstFrameReady", module.TypeSystem.Boolean, playback.FieldType) { HasThis = true });
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, playback); il.Emit(OpCodes.Callvirt, new MethodReference("get_AudioFinished", module.TypeSystem.Boolean, playback.FieldType) { HasThis = true });
        il.Emit(OpCodes.Call, module.ImportReference(typeof(StreamedAudio).GetMethod(nameof(StreamedAudio.Ready))!)); il.Emit(OpCodes.Ret);

        var clock = new MethodDefinition("SwitchMovieClock", MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Double);
        clock.Parameters.Add(new ParameterDefinition("elapsed", ParameterAttributes.None, module.TypeSystem.Double));
        clock.Parameters.Add(new ParameterDefinition("clip", ParameterAttributes.None, clip));
        clock.Parameters.Add(new ParameterDefinition("muted", ParameterAttributes.None, module.TypeSystem.Boolean));
        cut.Methods.Add(clock); var ci = clock.Body.GetILProcessor();
        var silent = ci.Create(OpCodes.Ldarg_0);
        ci.Emit(OpCodes.Ldarg_1); ci.Emit(OpCodes.Callvirt, getSound); ci.Emit(OpCodes.Brfalse, silent);
        ci.Emit(OpCodes.Ldarg_0); ci.Emit(OpCodes.Ldarg_1); ci.Emit(OpCodes.Callvirt, getSound); ci.Emit(OpCodes.Callvirt, getSamples);
        ci.Emit(OpCodes.Ldarg_2); ci.Emit(OpCodes.Call, module.ImportReference(typeof(StreamedAudio).GetMethod(nameof(StreamedAudio.MediaTime))!)); ci.Emit(OpCodes.Ret);
        ci.Append(silent); ci.Emit(OpCodes.Ret); clock.Body.MaxStackSize = 4;
        var update = cut.Methods.Single(m => m.Name == "Update");
        var fps = update.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "get_Fps");
        var sub = fps.Previous.Previous.Previous;
        if (sub.OpCode != OpCodes.Sub || fps.Previous.OpCode != OpCodes.Ldfld || fps.Previous.Previous.OpCode != OpCodes.Ldarg_0)
            throw new InvalidDataException("Movie clock expression changed");
        var target = sub.Next; var ui = update.Body.GetILProcessor();
        ui.InsertBefore(target, ui.Create(OpCodes.Ldarg_0)); ui.InsertBefore(target, ui.Create(OpCodes.Ldfld, cut.Fields.Single(f => f.Name == "_clip")));
        ui.InsertBefore(target, ui.Create(OpCodes.Ldsfld, module.GetType("DuelDx.GameWindow").Fields.Single(f => f.Name == "Muted")));
        ui.InsertBefore(target, ui.Create(OpCodes.Call, clock)); update.Body.MaxStackSize += 3;
        PlatformPatches.ExpandShortBranches(update);
        log("PASS Bink starts with 750ms PCM prebuffer; movie source clock bounded by mixed PCM during decoder refill; muted/no-audio paths preserved");
    }
}
