using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class DialoguePatches
{
    internal static void Apply(ModuleDefinition module, Action<string> log)
    {
        var window = module.GetType("DuelDx.GameWindow");
        var talk = module.GetType("DuelDx.TalkBox");
        var compose = window.Methods.Single(m => m.Name == "Compose");
        var receiver = compose.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "get_Tlk");
        var draw = compose.Body.Instructions.Single(i => i.Operand is MethodReference m && m.DeclaringType == talk && m.Name == "DrawTalk");
        var method = new MethodDefinition("DrawBattleTalk", MethodAttributes.Private, module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("layer", ParameterAttributes.None, module.TypeSystem.Boolean)); window.Methods.Add(method);
        var il = method.Body.GetILProcessor(); var ret = il.Create(OpCodes.Ret);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, window.Methods.Single(m => m.Name == "get_FieldOpen")); il.Emit(OpCodes.Brtrue, ret);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, (MethodReference)receiver.Operand); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, (MethodReference)draw.Operand); il.Append(ret);
        receiver.OpCode = OpCodes.Nop; receiver.Operand = null; draw.OpCode = OpCodes.Call; draw.Operand = method;

        var advance = talk.Methods.Single(m => m.Name == "AdvanceTalk");
        var host = talk.Fields.Single(f => f.FieldType == window);
        var w = talk.NestedTypes.Single(t => t.Name == "TalkWindow");
        var writer = advance.Body.GetILProcessor(); var first = advance.Body.Instructions[0];
        void Add(Instruction i) => writer.InsertBefore(first, i);
        void Times()
        {
            foreach (string field in new[] { "_lastTime", "_realTime", "_gameSpeed" })
            { Add(writer.Create(OpCodes.Ldarg_0)); Add(writer.Create(OpCodes.Ldfld, host)); Add(writer.Create(OpCodes.Ldfld, window.Fields.Single(f => f.Name == field))); }
        }
        Add(writer.Create(OpCodes.Ldarg_1)); Add(writer.Create(OpCodes.Ldarg_2)); Times();
        Add(writer.Create(OpCodes.Call, module.ImportReference(typeof(DialogueClock).GetMethod(nameof(DialogueClock.Tick))!)));
        Add(writer.Create(OpCodes.Starg, advance.Parameters[1]));
        Add(writer.Create(OpCodes.Ldarg_1));
        foreach (string field in new[] { "Voice", "VoiceLoaded", "Revealed" })
        { Add(writer.Create(OpCodes.Ldarg_1)); Add(writer.Create(OpCodes.Ldfld, w.Fields.Single(f => f.Name == field))); }
        Add(writer.Create(OpCodes.Call, module.ImportReference(typeof(DialogueClock).GetMethod(nameof(DialogueClock.Observe))!)));
        // Use the local text clock only for voice-paced reveal inside this method.
        foreach (var read in advance.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == "_lastTime").ToArray())
        {
            // The injected prefix also reads game time; only rewrite the original body.
            if (advance.Body.Instructions.IndexOf(read) < advance.Body.Instructions.IndexOf(first)) continue;
            var start = read.Previous.Previous;
            if (start.OpCode != OpCodes.Ldarg_0 || read.Previous.Operand != host) throw new InvalidDataException("Dialogue clock access changed");
            writer.InsertBefore(start, writer.Create(OpCodes.Ldarg_1));
            var after = read.Next;
            foreach (string field in new[] { "_realTime", "_gameSpeed" })
            {
                writer.InsertBefore(after, writer.Create(OpCodes.Ldarg_0)); writer.InsertBefore(after, writer.Create(OpCodes.Ldfld, host));
                writer.InsertBefore(after, writer.Create(OpCodes.Ldfld, window.Fields.Single(f => f.Name == field)));
            }
            writer.InsertBefore(after, writer.Create(OpCodes.Call, module.ImportReference(typeof(DialogueClock).GetMethod(nameof(DialogueClock.Now))!)));
        }
        // loaded==0 formerly returned without displaying text. Route that return
        // to the existing unvoiced typewriter, retaining the pending voice state.
        var waitingReturn = advance.Body.Instructions.Single(i => i.OpCode == OpCodes.Ret &&
            (i.Previous.OpCode == OpCodes.Brtrue || i.Previous.OpCode == OpCodes.Brtrue_S));
        // The typewriter begins at the load of textTick preceding TalkStepTicks math.
        var normal = advance.Body.Instructions.Last(i => i.OpCode == OpCodes.Ldarg_2);
        waitingReturn.OpCode = OpCodes.Br; waitingReturn.Operand = normal;
        advance.Body.MaxStackSize = Math.Max(advance.Body.MaxStackSize, 12);
        foreach (var item in new[] { (talk, "DrawTalkWindow", 0), (talk, "DrawTalkLines", 1), (talk, "DrawUiScaled", 2), (talk, "DrawTalkFaceRow", 3),
            (module.GetType("DuelDx.FieldScene"), "DrawFieldLayers", 4), (module.GetType("DuelDx.FieldScene"), "DrawFieldBackdrop", 5) })
        {
            var target = item.Item1.Methods.Single(m => m.Name == item.Item2); var t = target.Body.GetILProcessor(); var entry = target.Body.Instructions[0];
            t.InsertBefore(entry, t.Create(OpCodes.Ldc_I4, item.Item3)); t.InsertBefore(entry, t.Create(OpCodes.Call, module.ImportReference(typeof(DetailProfiler).GetMethod(nameof(DetailProfiler.Begin))!)));
            foreach (var end in target.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
            {
                end.OpCode = OpCodes.Ldc_I4; end.Operand = item.Item3;
                var call = t.Create(OpCodes.Call, module.ImportReference(typeof(DetailProfiler).GetMethod(nameof(DetailProfiler.End))!));
                t.InsertAfter(end, call); t.InsertAfter(call, t.Create(OpCodes.Ret));
            }
            target.Body.MaxStackSize = Math.Max(2, target.Body.MaxStackSize);
        }
        log("PASS field talk drawn once; text reveals during voice preparation; local text clock catches short frame stalls, excludes long absences; nested detail timers enabled");
    }
}
