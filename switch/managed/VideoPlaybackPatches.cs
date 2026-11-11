using Mono.Cecil;
using Mono.Cecil.Cil;
namespace WogSwitch;
internal static class VideoPlaybackPatches
{
    internal static void Apply(ModuleDefinition module, Action<string> log)
    {
        var window = module.GetType("DuelDx.GameWindow");
        var cut = module.GetType("DuelDx.CutscenePlayer");
        var compose = window.Methods.Single(m => m.Name == "Compose");
        var il = compose.Body.GetILProcessor();
        var original = compose.Body.Instructions[0];
        var getCut = window.Methods.Single(m => m.Name == "get_Cut");
        void Add(Instruction i) => il.InsertBefore(original, i);
        Add(il.Create(OpCodes.Ldarg_0)); Add(il.Create(OpCodes.Call, getCut));
        Add(il.Create(OpCodes.Call, cut.Methods.Single(m => m.Name == "get_Playing")));
        Add(il.Create(OpCodes.Brfalse, original));
        Add(il.Create(OpCodes.Ldarg_0)); Add(il.Create(OpCodes.Call, getCut));
        Add(il.Create(OpCodes.Call, cut.Methods.Single(m => m.Name == "get_Showing")));
        Add(il.Create(OpCodes.Brfalse, original));
        var begin = compose.Body.Instructions.First(i => i.Operand is MethodReference m && m.Name == "Begin" && m.DeclaringType.Name == "HiCanvas");
        var hiGetter = compose.Body.Instructions.Take(compose.Body.Instructions.IndexOf(begin)).Last(i => i.Operand is MethodReference m && m.Name == "get_Hi");
        Add(il.Create(OpCodes.Ldarg_0)); Add(il.Create(OpCodes.Call, (MethodReference)hiGetter.Operand));
        Add(il.Create(OpCodes.Call, (MethodReference)begin.Operand));
        Add(il.Create(OpCodes.Ldc_I4, 10)); Add(il.Create(OpCodes.Call, module.ImportReference(typeof(FrameProfiler).GetMethod(nameof(FrameProfiler.Begin))!)));
        Add(il.Create(OpCodes.Ldarg_0)); Add(il.Create(OpCodes.Call, getCut));
        Add(il.Create(OpCodes.Call, cut.Methods.Single(m => m.Name == "Draw")));
        Add(il.Create(OpCodes.Ldc_I4, 10)); Add(il.Create(OpCodes.Call, module.ImportReference(typeof(FrameProfiler).GetMethod(nameof(FrameProfiler.End))!)));
        Add(il.Create(OpCodes.Ret));
        log("PASS active fullscreen movie bypasses covered scene composition; pending/not-showing movies preserve normal path");

        var paint = cut.Methods.Single(m => m.Name == "PaintFrame");
        var writer = paint.Body.GetILProcessor(); var first = paint.Body.Instructions[0];
        var host = cut.Fields.Single(f => f.FieldType == window);
        void P(Instruction i) => writer.InsertBefore(first, i);
        void HostField(string field) { P(writer.Create(OpCodes.Ldarg_0)); P(writer.Create(OpCodes.Ldfld, host)); P(writer.Create(OpCodes.Ldfld, window.Fields.Single(f => f.Name == field))); }
        void HostGetter(string method) { P(writer.Create(OpCodes.Ldarg_0)); P(writer.Create(OpCodes.Ldfld, host)); P(writer.Create(OpCodes.Call, window.Methods.Single(m => m.Name == method))); }
        HostField("_fb"); HostGetter("get_BoardWidth"); HostGetter("get_BoardHeight");
        foreach (string name in new[] { "_frame", "_frameW", "_frameH" }) { P(writer.Create(OpCodes.Ldarg_0)); P(writer.Create(OpCodes.Ldfld, cut.Fields.Single(f => f.Name == name))); }
        HostField("_camX"); HostField("_camY"); HostGetter("get_ViewWidth"); HostGetter("get_ViewHeight");
        P(writer.Create(OpCodes.Call, module.ImportReference(typeof(SwitchRaster).GetMethod(nameof(SwitchRaster.TryVideoCopy))!)));
        P(writer.Create(OpCodes.Brfalse, first)); P(writer.Create(OpCodes.Ret));
        paint.Body.MaxStackSize = Math.Max(paint.Body.MaxStackSize, 12);
        log("PASS movie frame matching viewport uses native opaque copy; scaled frames retain original fallback");
    }
}
