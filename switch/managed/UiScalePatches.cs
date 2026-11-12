using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class UiScalePatches
{
    internal static VariableDefinition Local(MethodDefinition m,Instruction i)=>i.OpCode.Code switch{
        Code.Ldloc_0 or Code.Stloc_0=>m.Body.Variables[0],Code.Ldloc_1 or Code.Stloc_1=>m.Body.Variables[1],Code.Ldloc_2 or Code.Stloc_2=>m.Body.Variables[2],Code.Ldloc_3 or Code.Stloc_3=>m.Body.Variables[3],
        Code.Ldloc or Code.Ldloc_S or Code.Ldloca or Code.Ldloca_S or Code.Stloc or Code.Stloc_S=>(VariableDefinition)i.Operand,_=>throw new InvalidDataException("Raster local changed")};
    internal static void Apply(ModuleDefinition module,Action<string> log)
    {
        var window=module.GetType("DuelDx.GameWindow");var talk=module.GetType("DuelDx.TalkBox");var host=talk.Fields.Single(f=>f.FieldType==window);
        foreach(var target in new[]{(window,"BlitScaled"),(talk,"DrawTalkFace"),(talk,"DrawUiScaled")}){
            var method=target.Item1.Methods.Single(m=>m.Name==target.Item2);var il=method.Body.GetILProcessor();Instruction first;VariableDefinition? frame=null;
            var all=module.GetTypes().SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>();
            TypeReference frameType;
            if(target.Item2=="DrawUiScaled"){
                var pixels=method.Body.Instructions.First(i=>i.Operand is MethodReference r&&r.Name=="get_Px");frame=Local(method,pixels.Previous);frameType=frame.VariableType;
                var clip=method.Body.Instructions.Single(i=>i.Operand is FieldReference f&&f.Name=="_uiClip");first=clip.Next.Next;
            }else{frameType=method.Parameters[0].ParameterType;first=method.Body.Instructions[0];}
            void E(Instruction i)=>il.InsertBefore(first,i);
            void Host(){E(il.Create(OpCodes.Ldarg_0));if(target.Item1==talk)E(il.Create(OpCodes.Ldfld,host));}
            Host();E(il.Create(OpCodes.Ldfld,window.Fields.Single(f=>f.Name=="_fb")));
            foreach(string getter in new[]{"get_BoardWidth","get_BoardHeight"}){Host();E(il.Create(OpCodes.Call,window.Methods.Single(m=>m.Name==getter)));}
            void Frame(string name){
                if(frame!=null)E(il.Create(frameType.IsValueType?OpCodes.Ldloca:OpCodes.Ldloc,frame));else E(il.Create(frameType.IsValueType?OpCodes.Ldarga:OpCodes.Ldarg,method.Parameters[0]));
                E(il.Create(OpCodes.Call,all.First(m=>m.Name=="get_"+name&&m.DeclaringType.FullName==frameType.FullName)));
            }
            foreach(string name in new[]{"Px","W","H"})Frame(name);
            if(target.Item2=="DrawUiScaled"){
                Frame("X");Frame("Y");foreach(int p in new[]{2,3,4,5,6,7,8,9})E(il.Create(OpCodes.Ldarg,method.Parameters[p]));
                Host();E(il.Create(OpCodes.Ldfld,window.Fields.Single(f=>f.Name=="_uiClip")));
                E(il.Create(OpCodes.Call,module.ImportReference(typeof(UiScaleRaster).GetMethod(nameof(UiScaleRaster.TryUi))!)));
            }else{
                E(il.Create(OpCodes.Ldarg,method.Parameters[1]));E(il.Create(OpCodes.Ldarg,method.Parameters[2]));
                E(il.Create(OpCodes.Ldarg,method.Parameters[3]));
                if(target.Item2=="DrawTalkFace")E(il.Create(OpCodes.Ldarg,method.Parameters[3]));else E(il.Create(OpCodes.Ldarg,method.Parameters[4]));
                if(target.Item2=="BlitScaled")E(il.Create(OpCodes.Ldarg,method.Parameters[5]));else E(il.Create(OpCodes.Ldc_I4_0));
                if(target.Item2=="DrawTalkFace")E(il.Create(OpCodes.Ldarg,method.Parameters[4]));else E(il.Create(OpCodes.Ldc_I4_0));
                if(target.Item2=="DrawTalkFace"){Host();E(il.Create(OpCodes.Ldfld,window.Fields.Single(f=>f.Name=="_uiClip")));}
                else {var clipType=window.Fields.Single(f=>f.Name=="_uiClip").FieldType;var clip=new VariableDefinition(clipType);method.Body.Variables.Add(clip);method.Body.InitLocals=true;E(il.Create(OpCodes.Ldloc,clip));}
                E(il.Create(OpCodes.Call,module.ImportReference(typeof(UiScaleRaster).GetMethod(nameof(UiScaleRaster.TryPortrait))!)));
            }
            E(il.Create(OpCodes.Brfalse,first));E(il.Create(OpCodes.Ret));method.Body.MaxStackSize=Math.Max(24,method.Body.MaxStackSize);PlatformPatches.ExpandShortBranches(method);
        }
        log("PASS native scaled talk UI and portraits with exact clipping/tint; faded global portraits retain original fallback");
    }
}
