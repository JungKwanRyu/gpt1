using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class VoiceSyncPatches
{
    internal static void Apply(ModuleDefinition module,Action<string> log)
    {
        var talk=module.GetType("DuelDx.TalkBox");var window=module.GetType("DuelDx.GameWindow");
        var w=talk.NestedTypes.Single(t=>t.Name=="TalkWindow");var host=talk.Fields.Single(f=>f.FieldType==window);
        var advance=talk.Methods.Single(m=>m.Name=="AdvanceTalk");var il=advance.Body.GetILProcessor();
        var divide=advance.Body.Instructions.Single(i=>i.OpCode==OpCodes.Div&&i.Previous.OpCode==OpCodes.Conv_R8&&i.Previous.Previous.Operand is FieldReference f&&f.Name=="VoiceSeconds");
        var next=divide.Next;
        il.InsertBefore(next,il.Create(OpCodes.Ldarg_1));il.InsertBefore(next,il.Create(OpCodes.Ldfld,w.Fields.Single(f=>f.Name=="VoiceTag")));
        il.InsertBefore(next,il.Create(OpCodes.Ldarg_1));il.InsertBefore(next,il.Create(OpCodes.Ldfld,w.Fields.Single(f=>f.Name=="VoiceSeconds")));il.InsertBefore(next,il.Create(OpCodes.Conv_R8));
        il.InsertBefore(next,il.Create(OpCodes.Call,module.ImportReference(typeof(VoicePlayback).GetMethod(nameof(VoicePlayback.Fraction))!)));
        PlatformPatches.ExpandShortBranches(advance);
        var load=window.Methods.Single(m=>m.Name=="LoadClip");var sound=((GenericInstanceType)load.Parameters[1].ParameterType).GenericArguments[0];
        var subscribe=new GenericInstanceMethod(module.ImportReference(typeof(ClipJobs).GetMethod(nameof(ClipJobs.Subscribe))!));subscribe.GenericArguments.Add(sound);
        var li=load.Body.GetILProcessor();var first=load.Body.Instructions[0];var starts=new VariableDefinition(module.TypeSystem.Boolean);load.Body.Variables.Add(starts);load.Body.InitLocals=true;
        foreach(var op in new[]{OpCodes.Ldarg_0,OpCodes.Ldarg_1,OpCodes.Ldarg_2})li.InsertBefore(first,li.Create(op));
        li.InsertBefore(first,li.Create(OpCodes.Ldloca,starts));li.InsertBefore(first,li.Create(OpCodes.Call,subscribe));li.InsertBefore(first,li.Create(OpCodes.Starg,load.Parameters[1]));
        li.InsertBefore(first,li.Create(OpCodes.Ldloc,starts));li.InsertBefore(first,li.Create(OpCodes.Brtrue,first));li.InsertBefore(first,li.Create(OpCodes.Ret));
        load.Body.MaxStackSize=Math.Max(5,load.Body.MaxStackSize);PlatformPatches.ExpandShortBranches(load);
        var ignore=new MethodDefinition("SwitchIgnorePrefetchedVoice",MethodAttributes.Assembly|MethodAttributes.Static,module.TypeSystem.Void);
        ignore.Parameters.Add(new ParameterDefinition(sound));window.Methods.Add(ignore);ignore.Body.GetILProcessor().Emit(OpCodes.Ret);
        var open=talk.Methods.Single(m=>m.Name=="OpenTalkWindow");var oi=open.Body.GetILProcessor();var entry=open.Body.Instructions[0];
        void Add(Instruction i)=>oi.InsertBefore(entry,i);
        Add(oi.Create(OpCodes.Ldarg_1));Add(oi.Create(OpCodes.Ldfld,w.Fields.Single(f=>f.Name=="Voice")));Add(oi.Create(OpCodes.Ldc_I4_0));Add(oi.Create(OpCodes.Ble,entry));
        Add(oi.Create(OpCodes.Ldsfld,window.Fields.Single(f=>f.Name=="Muted")));Add(oi.Create(OpCodes.Brtrue,entry));
        Add(oi.Create(OpCodes.Ldarg_0));Add(oi.Create(OpCodes.Ldfld,host));Add(oi.Create(OpCodes.Ldarg_1));Add(oi.Create(OpCodes.Ldfld,w.Fields.Single(f=>f.Name=="Voice")));
        Add(oi.Create(OpCodes.Ldnull));Add(oi.Create(OpCodes.Ldftn,ignore));
        var ctor=new MethodReference(".ctor",module.TypeSystem.Void,load.Parameters[1].ParameterType){HasThis=true};ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
        Add(oi.Create(OpCodes.Newobj,ctor));Add(oi.Create(OpCodes.Call,load));open.Body.MaxStackSize=Math.Max(5,open.Body.MaxStackSize);PlatformPatches.ExpandShortBranches(open);
        log("PASS dialogue fraction follows device-released PCM; early voice preparation coalesces duplicate requests; missing/muted output keeps original timing");
    }
}
