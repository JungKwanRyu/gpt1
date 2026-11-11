using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class WipeRasterPatches
{
    internal static void Apply(ModuleDefinition module,Action<string> log)
    {
        var type=module.GetType("DuelDx.FieldScene");var window=module.GetType("DuelDx.GameWindow");
        foreach(var item in new[]{("DrawSlideWipe",901),("DrawCombWipe",903),("DrawStreakWipe",904),("DrawGatherWipe",907),("DrawLineWipe",908),("DrawDissolveWipe",909)}) {
            var core=type.Methods.Single(m=>m.Name==item.Item1);string full=core.FullName;
            var calls=module.GetTypes().SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.Operand is MethodReference r && r.FullName==full).ToArray();
            var wrapper=new MethodDefinition(core.Name,core.Attributes,core.ReturnType);
            foreach(var p in core.Parameters)wrapper.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,p.ParameterType));
            type.Methods.Add(wrapper);core.Name="SwitchOriginal"+core.Name;
            var il=wrapper.Body.GetILProcessor();var fallback=il.Create(OpCodes.Ldarg_0);
            void Host(){il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,type.Fields.Single(f=>f.FieldType==window));}
            Host();il.Emit(OpCodes.Ldfld,window.Fields.Single(f=>f.Name=="_fb"));
            foreach(string name in new[]{"get_BoardWidth","get_BoardHeight"}){Host();il.Emit(OpCodes.Call,window.Methods.Single(m=>m.Name==name));}
            var wipe=wrapper.Parameters[2].ParameterType.Resolve();
            void Get(string name){il.Emit(OpCodes.Ldarg,wrapper.Parameters[2]);il.Emit(OpCodes.Call,wipe.Methods.Single(m=>m.Name=="get_"+name));}
            Get("Base");Get("Over");il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Ldc_I4,item.Item2);Get("Way");
            il.Emit(OpCodes.Ldarg,wrapper.Parameters[3]);Get("Ticks");
            il.Emit(OpCodes.Call,module.ImportReference(typeof(SwitchRaster).GetMethod(nameof(SwitchRaster.TryWipe))!));
            il.Emit(OpCodes.Brfalse,fallback);il.Emit(OpCodes.Ret);il.Append(fallback);
            foreach(var p in wrapper.Parameters)il.Emit(OpCodes.Ldarg,p);
            il.Emit(OpCodes.Call,core);il.Emit(OpCodes.Ret);wrapper.Body.MaxStackSize=16;
            foreach(var call in calls)call.Operand=wrapper;
            log("PASS native field wipe with preserved original fallback: "+item.Item1);
        }
    }
}
