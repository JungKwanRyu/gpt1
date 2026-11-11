using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WogSwitch;

internal static class AudioMixerPatches
{
    internal static void Apply(ModuleDefinition module, Action<string> log)
    {
        var mixer = module.GetType("WarOfGenesis.Assets.AudioMixer");
        var core = mixer.Methods.Single(m => m.Name == "Mix" && m.IsStatic && m.Parameters.Count == 3);
        var voice = mixer.NestedTypes.Single(t => t.Name == "Voice");
        var sound = voice.Fields.Single(f => f.Name == "Sound");
        var reads = core.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        var samples = reads.First(m => m.Name == "get_Samples");
        var channels = reads.First(m => m.Name == "get_Channels");
        if (samples.ReturnType.FullName != "System.Int16[]") throw new InvalidDataException("Mixer PCM sample representation changed");
        string signature = core.FullName;
        var callers = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Where(i => i.Operand is MethodReference r && r.FullName == signature).ToArray();
        if (callers.Length < 2) throw new InvalidDataException("Mixer voice call sites changed");
        var wrapper = new MethodDefinition("Mix", core.Attributes, core.ReturnType);
        foreach (var p in core.Parameters) wrapper.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
        mixer.Methods.Add(wrapper);
        wrapper.Body.InitLocals = true;
        var alive = new VariableDefinition(module.TypeSystem.Boolean); wrapper.Body.Variables.Add(alive);
        var il = wrapper.Body.GetILProcessor();
        void Field(string name) { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, voice.Fields.Single(f => f.Name == name)); }
        foreach (var getter in new[] { samples, channels })
        { Field("Sound"); il.Emit(OpCodes.Callvirt, getter); }
        Field("Step");
        foreach (string pan in new[] { "Left", "Right" })
        { Field("Gain"); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Mul); Field(pan); il.Emit(OpCodes.Mul); }
        Field("Loop");
        foreach (string state in new[] { "Position", "Laps" })
        { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldflda, voice.Fields.Single(f => f.Name == state)); }
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloca, alive);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(SwitchMixer).GetMethod(nameof(SwitchMixer.TryMix))!));
        var fallback = il.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, fallback); il.Emit(OpCodes.Ldloc, alive); il.Emit(OpCodes.Ret);
        il.Append(fallback); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
        core.Name = "SwitchOriginalMix";
        il.Emit(OpCodes.Call, core); il.Emit(OpCodes.Ret); wrapper.Body.MaxStackSize = 16;
        foreach (var call in callers) call.Operand = wrapper;
        log("PASS native per-voice PCM interpolation with original fallback; playback clock, voice state and locking policy preserved");
    }
}
