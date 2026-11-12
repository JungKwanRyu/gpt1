using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class DecoderPatches
{
    internal static void Apply(ModuleDefinition module,Action<string> log)
    {
        var fft=module.GetType("WarOfGenesis.Assets.BinkAudio").NestedTypes.Single(t=>t.Name=="Fft");
        var method=fft.Methods.Single(m=>m.Name=="InverseRdft");
        var il=method.Body.GetILProcessor();var original=method.Body.Instructions[0];
        il.InsertBefore(original,il.Create(OpCodes.Ldarg_1));
        foreach(string name in new[]{"_re","_im","_rev","_cos","_sin","_n"}) {
            il.InsertBefore(original,il.Create(OpCodes.Ldarg_0));il.InsertBefore(original,il.Create(OpCodes.Ldfld,fft.Fields.Single(f=>f.Name==name)));
        }
        il.InsertBefore(original,il.Create(OpCodes.Call,module.ImportReference(typeof(NativeAudioDecoder).GetMethod(nameof(NativeAudioDecoder.TryRdft))!)));
        il.InsertBefore(original,il.Create(OpCodes.Brfalse,original));il.InsertBefore(original,il.Create(OpCodes.Ret));
        method.Body.MaxStackSize=Math.Max(8,method.Body.MaxStackSize);PlatformPatches.ExpandShortBranches(method);
        log("PASS native Bink inverse RDFT for both movie and dialogue audio; original decoder fallback preserved");
    }
}
