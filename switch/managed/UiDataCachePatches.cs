using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class UiDataCachePatches
{
    internal static void Apply(ModuleDefinition module,Action<string> log)
    {
        var slots=module.GetType("DuelDx.SlotsScreen");
        var mail=module.GetType("DuelDx.MosesScene");
        Wrap(mail.Methods.Single(m=>m.Name=="WrapText"),true);
        Wrap(slots.Methods.Single(m=>m.Name=="SlotHead"),false);
        foreach(string name in new[]{"OpenSlots","SaveSlot","AutoSave"}){
            var m=slots.Methods.Single(m=>m.Name==name);var il=m.Body.GetILProcessor();var first=m.Body.Instructions[0];
            il.InsertBefore(first,il.Create(OpCodes.Ldarg_0));il.InsertBefore(first,il.Create(OpCodes.Call,module.ImportReference(typeof(UiDataCache).GetMethod("Clear")!)));
            PlatformPatches.ExpandShortBranches(m);
        }
        int saves=0;
        foreach(var m in module.GetType("DuelDx.SystemMenu").Methods.Where(m=>m.HasBody))
            foreach(var i in m.Body.Instructions)
                if(i.Operand is GenericInstanceMethod g&&g.Name=="Serialize"&&g.DeclaringType.FullName=="System.Text.Json.JsonSerializer"&&g.Parameters.Count==2&&g.ReturnType.FullName=="System.String"){
                    var replacement=new GenericInstanceMethod(module.ImportReference(typeof(UiDataCache).GetMethod("Serialize")!));replacement.GenericArguments.Add(g.GenericArguments[0]);i.Operand=replacement;saves++;
                }
        if(saves!=2)throw new InvalidDataException("Expected two save serialization sites: "+saves);
        log("PASS bounded mail layout reuse; draw-only save header reuse; compact save JSON retains original synchronous state capture");
        void Wrap(MethodDefinition core,bool isMail){
            var callers=module.GetTypes().SelectMany(t=>t.Methods).Where(m=>m.HasBody&&(isMail||m.DeclaringType==slots&&m.Name=="DrawSlots"))
                .SelectMany(m=>m.Body.Instructions).Where(i=>i.Operand is MethodReference r&&r.FullName==core.FullName).ToArray();
            var wrapper=new MethodDefinition(core.Name,core.Attributes,core.ReturnType);
            foreach(var p in core.Parameters)wrapper.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,p.ParameterType));
            core.DeclaringType.Methods.Add(wrapper);var il=wrapper.Body.GetILProcessor();wrapper.Body.InitLocals=true;
            var result=new VariableDefinition(core.ReturnType);wrapper.Body.Variables.Add(result);
            MethodReference Bridge(string name){var g=new GenericInstanceMethod(module.ImportReference(typeof(UiDataCache).GetMethod(name)!));g.GenericArguments.Add(core.ReturnType);return g;}
            void Key(){il.Emit(OpCodes.Ldarg_0);foreach(var p in wrapper.Parameters)il.Emit(OpCodes.Ldarg,p);if(!isMail)il.Emit(OpCodes.Call,core.DeclaringType.Methods.Single(m=>m.Name=="SlotPath"));}
            var miss=il.Create(OpCodes.Ldarg_0);Key();il.Emit(OpCodes.Ldloca,result);il.Emit(OpCodes.Call,Bridge(isMail?"MailTry":"SlotTry"));il.Emit(OpCodes.Brfalse,miss);il.Emit(OpCodes.Ldloc,result);il.Emit(OpCodes.Ret);
            il.Append(miss);foreach(var p in wrapper.Parameters)il.Emit(OpCodes.Ldarg,p);il.Emit(OpCodes.Call,core);il.Emit(OpCodes.Stloc,result);Key();il.Emit(OpCodes.Ldloc,result);il.Emit(OpCodes.Call,Bridge(isMail?"MailPut":"SlotPut"));il.Emit(OpCodes.Ret);
            core.Name="SwitchOriginal"+core.Name;foreach(var c in callers)c.Operand=wrapper;wrapper.Body.MaxStackSize=8;
        }
    }
}
