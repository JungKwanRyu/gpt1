using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class AudioReusePatches
{
    internal static void Apply(ModuleDefinition module,Action<string> log){
        var type=module.GetType("WarOfGenesis.Assets.BinkAudio");var core=type.Methods.Single(m=>m.Name=="Decode");
        var range=core.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.Name=="GetRange");var array=range.Next;
        if(array.Operand is not MethodReference ar||ar.Name!="ToArray")throw new InvalidDataException("Bink PCM copy pattern changed");
        range.OpCode=OpCodes.Call;range.Operand=module.ImportReference(typeof(AudioReuse).GetMethod("CopyRange")!);array.OpCode=OpCodes.Nop;array.Operand=null;
        var callers=module.GetTypes().SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.Operand is MethodReference r&&r.FullName==core.FullName).ToArray();
        var wrapper=new MethodDefinition("Decode",core.Attributes,core.ReturnType);type.Methods.Add(wrapper);var il=wrapper.Body.GetILProcessor();var result=new VariableDefinition(core.ReturnType);wrapper.Body.Variables.Add(result);wrapper.Body.InitLocals=true;
        var data=type.Fields.Single(f=>f.Name=="_data");
        MethodReference Bridge(string name){var m=new GenericInstanceMethod(module.ImportReference(typeof(AudioReuse).GetMethod(name)!));m.GenericArguments.Add(core.ReturnType);return m;}
        void Data(){il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,data);}
        var miss=il.Create(OpCodes.Ldarg_0);Data();il.Emit(OpCodes.Ldloca,result);il.Emit(OpCodes.Call,Bridge("Try"));il.Emit(OpCodes.Brfalse,miss);il.Emit(OpCodes.Ldloc,result);il.Emit(OpCodes.Ret);
        il.Append(miss);il.Emit(OpCodes.Call,core);il.Emit(OpCodes.Stloc,result);Data();il.Emit(OpCodes.Ldloc,result);il.Emit(OpCodes.Ldloc,result);
        var sound=module.GetType("WarOfGenesis.Assets.PcmSound");il.Emit(OpCodes.Callvirt,sound.Methods.Single(m=>m.Name=="get_Samples"));il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_I4);il.Emit(OpCodes.Call,Bridge("Put"));il.Emit(OpCodes.Ret);core.Name="SwitchOriginalDecode";
        foreach(var c in callers)c.Operand=wrapper;wrapper.Body.MaxStackSize=5;
        log("PASS content-keyed decoded audio reuse bounded to 64MiB/eight clips; single PCM tail copy");
    }
}
