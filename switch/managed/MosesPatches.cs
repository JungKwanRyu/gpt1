using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class MosesPatches
{
    internal static void Apply(ModuleDefinition module,Action<string> log)
    {
        var owner=module.GetType("DuelDx.MosesScene");var window=module.GetType("DuelDx.GameWindow");var host=owner.Fields.Single(f=>f.FieldType==window);
        var draw=owner.Methods.Single(m=>m.Name=="DrawMoses");var code=draw.Body.Instructions;
        var start=code.First(i=>i.Operand is FieldReference f&&f.Name=="_mosesBlueFade").Previous;
        var end=code.Single(i=>i.Operand is MethodReference m&&m.Name=="StepMosesSystemSwitch").Previous;
        if(start.OpCode!=OpCodes.Ldarg_0||end.OpCode!=OpCodes.Ldarg_0)throw new InvalidDataException("Moses fade boundary changed");
        var region=code.Skip(code.IndexOf(start)).Take(code.IndexOf(end)-code.IndexOf(start)).ToArray();var removed=region.ToHashSet();
        var reference=new MethodDefinition("SwitchOriginalMosesFade",MethodAttributes.Assembly,module.TypeSystem.Void);owner.Methods.Add(reference);reference.Body.InitLocals=true;
        var vars=draw.Body.Variables.ToDictionary(v=>v,v=>new VariableDefinition(v.VariableType));foreach(var v in vars.Values)reference.Body.Variables.Add(v);
        var ret=Instruction.Create(OpCodes.Ret);var map=region.ToDictionary(i=>i,i=>Instruction.Create(OpCodes.Nop));
        foreach(var old in region){var copy=map[old];copy.OpCode=old.OpCode;copy.Operand=old.Operand switch{
            VariableDefinition v=>vars[v],Instruction i=>i==end?ret:map.TryGetValue(i,out var target)?target:throw new InvalidDataException("Moses fade jumps outside verified range"),
            Instruction[] jumps=>jumps.Select(i=>i==end?ret:map[i]).ToArray(),_=>old.Operand};reference.Body.Instructions.Add(copy);}
        reference.Body.Instructions.Add(ret);reference.Body.MaxStackSize=draw.Body.MaxStackSize;PlatformPatches.ExpandShortBranches(reference);
        var helper=new MethodDefinition("SwitchMosesFade",MethodAttributes.Assembly,module.TypeSystem.Void);owner.Methods.Add(helper);helper.Body.InitLocals=true;
        var origin=owner.Methods.Single(m=>m.Name=="MosesOrigin");var xy=new VariableDefinition(origin.ReturnType);helper.Body.Variables.Add(xy);var il=helper.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,origin);il.Emit(OpCodes.Stloc,xy);il.Emit(OpCodes.Ldarg_0);
        void Host(){il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,host);}
        Host();il.Emit(OpCodes.Ldfld,window.Fields.Single(f=>f.Name=="_fb"));
        foreach(string name in new[]{"get_BoardWidth","get_BoardHeight"}){Host();il.Emit(OpCodes.Call,window.Methods.Single(m=>m.Name==name));}
        Host();il.Emit(OpCodes.Ldfld,window.Fields.Single(f=>f.Name=="_camY"));Host();il.Emit(OpCodes.Call,window.Methods.Single(m=>m.Name=="get_ViewHeight"));
        foreach(string name in new[]{"Item1","Item2"}){il.Emit(OpCodes.Ldloca,xy);il.Emit(OpCodes.Ldfld,draw.Body.Instructions.Select(i=>i.Operand).OfType<FieldReference>().First(f=>f.Name==name&&f.DeclaringType.FullName==origin.ReturnType.FullName));}
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldflda,owner.Fields.Single(f=>f.Name=="_mosesFade"));
        foreach(string name in new[]{"_mosesBlueFade","_mosesBlackFade"}){il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,owner.Fields.Single(f=>f.Name==name));}
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldflda,owner.Fields.Single(f=>f.Name=="_mosesFadeFrom"));Host();il.Emit(OpCodes.Ldfld,window.Fields.Single(f=>f.Name=="_realTime"));
        il.Emit(OpCodes.Call,module.ImportReference(typeof(MosesRaster).GetMethod(nameof(MosesRaster.TryFade))!));var done=il.Create(OpCodes.Ret);
        il.Emit(OpCodes.Brtrue,done);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,reference);il.Append(done);helper.Body.MaxStackSize=16;
        var writer=draw.Body.GetILProcessor();var replacement=writer.Create(OpCodes.Ldarg_0);writer.InsertBefore(start,replacement);writer.InsertBefore(start,writer.Create(OpCodes.Call,helper));
        foreach(var instruction in code.Where(i=>!removed.Contains(i)).ToArray())if(instruction.Operand is Instruction jump&&removed.Contains(jump)){
            if(jump!=start)throw new InvalidDataException("External branch into Moses fade");instruction.Operand=replacement;}
        foreach(var old in region)writer.Remove(old);
        // The background inner row loop is exactly OR-alpha plus copy.
        var pixel=code.First(i=>i.OpCode==OpCodes.Stind_I4);var reads=code.Take(code.IndexOf(pixel)).Where(i=>i.Operand is MethodReference m&&m.Name=="get_Item"&&m.DeclaringType.FullName.StartsWith("System.Span`1")).TakeLast(2).ToArray();
        if(reads.Length!=2)throw new InvalidDataException("Moses background row access changed");
        var dst=UiScalePatches.Local(draw,reads[0].Previous.Previous);var src=UiScalePatches.Local(draw,reads[1].Previous.Previous);
        var body=reads[0].Previous.Previous;var back=code.Skip(code.IndexOf(pixel)).First(i=>ReferenceEquals(i.Operand,body));
        var begin=body.Previous.Previous.Previous;var after=back.Next;
        if(begin.OpCode!=OpCodes.Ldc_I4_0)throw new InvalidDataException("Moses row loop initialization changed");
        var row=code.Skip(code.IndexOf(begin)).Take(code.IndexOf(after)-code.IndexOf(begin)).ToHashSet();
        var load=writer.Create(OpCodes.Ldloc,src);writer.InsertBefore(begin,load);writer.InsertBefore(begin,writer.Create(OpCodes.Ldloc,dst));
        writer.InsertBefore(begin,writer.Create(OpCodes.Call,module.ImportReference(typeof(UiScaleRaster).GetMethod(nameof(UiScaleRaster.CopyOpaqueSpan))!)));
        foreach(var instruction in code.Where(i=>!row.Contains(i)).ToArray())if(instruction.Operand is Instruction jump&&row.Contains(jump)){
            if(jump!=begin)throw new InvalidDataException("External branch into Moses background row");instruction.Operand=load;}
        foreach(var old in row)writer.Remove(old);PlatformPatches.ExpandShortBranches(draw);
        log("PASS Moses background native rows and native crossfade/black/blue fade; transition follows elapsed active time at 30 ticks/sec; original fade retained");
    }
}
