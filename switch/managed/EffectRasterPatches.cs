using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class EffectRasterPatches
{
    internal static void Apply(ModuleDefinition module, Action<string> log)
    {
        var window=module.GetType("DuelDx.GameWindow"); var field=module.GetType("DuelDx.FieldScene");
        VariableDefinition Local(MethodDefinition m, Instruction i) => i.OpCode.Code switch {
            Code.Ldloc_0 or Code.Stloc_0 => m.Body.Variables[0], Code.Ldloc_1 or Code.Stloc_1 => m.Body.Variables[1],
            Code.Ldloc_2 or Code.Stloc_2 => m.Body.Variables[2], Code.Ldloc_3 or Code.Stloc_3 => m.Body.Variables[3],
            Code.Ldloc or Code.Ldloc_S or Code.Stloc or Code.Stloc_S => (VariableDefinition)i.Operand,
            _ => throw new InvalidDataException("Effect local changed: "+i) };
        foreach(string name in new[]{"DrawSceneFade","ApplyFieldGray","DrawFieldFade"}) {
            bool scene=name=="DrawSceneFade", gray=name=="ApplyFieldGray";
            var owner=scene?window:field; var method=owner.Methods.Single(m=>m.Name==name);
            var code=method.Body.Instructions; Instruction start; VariableDefinition? power=null,color=null;
            if(scene) {
                var dim=code.Single(i=>i.Operand is MethodReference r && r.Name=="Dim" && r.DeclaringType.Name=="HiCanvas");
                power=Local(method,dim.Previous.Previous); start=dim.Next;
            } else if(gray) {
                start=code.First(i=>i.OpCode.FlowControl==FlowControl.Cond_Branch).Next;
            } else {
                var get=code.Single(i=>i.Operand is MethodReference r && r.Name=="get_Color");
                color=Local(method,get.Next);start=get.Next.Next;
                var branch=code.Take(code.IndexOf(get)).Last(i=>i.OpCode.FlowControl==FlowControl.Cond_Branch);
                var candidate=branch.Previous;
                if(candidate.OpCode.Code is Code.Ldc_I4_0)candidate=candidate.Previous;
                power=Local(method,candidate);
            }
            var end=code.Last(); if(end.OpCode!=OpCodes.Ret)throw new InvalidDataException("Effect return changed");
            var removed=code.Skip(code.IndexOf(start)).Take(code.IndexOf(end)-code.IndexOf(start)).ToHashSet();
            foreach(var i in code.Where(i=>!removed.Contains(i)))
                if(i.Operand is Instruction jump && removed.Contains(jump))throw new InvalidDataException("External branch into effect loop");
            var il=method.Body.GetILProcessor();
            void E(Instruction i)=>il.InsertBefore(start,i);
            void Host(){E(il.Create(OpCodes.Ldarg_0));if(!scene)E(il.Create(OpCodes.Ldfld,field.Fields.Single(f=>f.FieldType==window)));}
            void F(string n){Host();E(il.Create(OpCodes.Ldfld,window.Fields.Single(f=>f.Name==n)));}
            void G(string n){Host();E(il.Create(OpCodes.Call,window.Methods.Single(m=>m.Name==n)));}
            F("_fb");G("get_BoardWidth");G("get_BoardHeight");
            if(scene||gray){F("_camX");F("_camY");G("get_ViewWidth");G("get_ViewHeight");}
            else{E(il.Create(OpCodes.Ldarg_1));E(il.Create(OpCodes.Ldarg_2));E(il.Create(OpCodes.Ldc_I4,640));E(il.Create(OpCodes.Ldc_I4,480));}
            E(il.Create(OpCodes.Ldc_I4,gray?0:scene?1:2));
            E(color==null?il.Create(OpCodes.Ldc_I4_0):il.Create(OpCodes.Ldloc,color));
            E(power==null?il.Create(OpCodes.Ldc_I4_0):il.Create(OpCodes.Ldloc,power));
            E(il.Create(OpCodes.Call,module.ImportReference(typeof(SwitchRaster).GetMethod(nameof(SwitchRaster.Transform))!)));
            foreach(var i in removed)il.Remove(i);
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,12);
            log("PASS native full-viewport effect: "+name);
        }
    }
}
