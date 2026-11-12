using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class FieldListPatches
{
    internal static void Apply(ModuleDefinition module,Action<string> log)
    {
        var field=module.GetType("DuelDx.FieldScene");var draw=field.Methods.Single(m=>m.Name=="DrawFieldLayers");
        var allocation=draw.Body.Instructions.Single(i=>i.OpCode==OpCodes.Newobj&&i.Operand is MethodReference m&&m.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1"));
        var ctor=(MethodReference)allocation.Operand;var scratch=new FieldDefinition("SwitchLayerScratch",FieldAttributes.Private,ctor.DeclaringType);field.Fields.Add(scratch);
        var helper=new MethodDefinition("SwitchLayerList",MethodAttributes.Private,ctor.DeclaringType);field.Methods.Add(helper);var il=helper.Body.GetILProcessor();var ready=il.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,scratch);il.Emit(OpCodes.Brtrue,ready);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Newobj,ctor);il.Emit(OpCodes.Stfld,scratch);il.Append(ready);
        il.Emit(OpCodes.Ldfld,scratch);il.Emit(OpCodes.Dup);il.Emit(OpCodes.Callvirt,new MethodReference("Clear",module.TypeSystem.Void,ctor.DeclaringType){HasThis=true});il.Emit(OpCodes.Ret);
        allocation.OpCode=OpCodes.Ldarg_0;allocation.Operand=null;draw.Body.GetILProcessor().InsertAfter(allocation,Instruction.Create(OpCodes.Call,helper));PlatformPatches.ExpandShortBranches(draw);
        log("PASS field layer scratch list/capacity reused; original visibility and sort order recalculated each draw");
    }
}
