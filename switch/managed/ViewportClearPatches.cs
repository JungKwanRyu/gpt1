using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WogSwitch;

internal static class ViewportClearPatches
{
    internal static void Apply(ModuleDefinition module, Action<string> log)
    {
        var window = module.GetType("DuelDx.GameWindow");
        var compose = window.Methods.Single(m => m.Name == "Compose");
        var begin = compose.Body.Instructions.First(i => i.Operand is MethodReference m && m.Name == "Begin" && m.DeclaringType.Name == "HiCanvas");
        var draw = compose.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "DrawBackground" && m.DeclaringType == window);
        var target = draw.Previous;
        if (target.OpCode != OpCodes.Ldarg_0 || compose.Body.Instructions.IndexOf(begin) >= compose.Body.Instructions.IndexOf(target))
            throw new InvalidDataException("Compose viewport clear boundary changed");
        var helper = new MethodDefinition("SwitchClearViewport", MethodAttributes.Assembly, module.TypeSystem.Boolean);
        window.Methods.Add(helper);
        var il = helper.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, window.Fields.Single(f => f.Name == "_fb"));
        foreach (string getter in new[] { "get_BoardWidth", "get_BoardHeight" })
        { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, window.Methods.Single(m => m.Name == getter)); }
        foreach (string field in new[] { "_camX", "_camY" })
        { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, window.Fields.Single(f => f.Name == field)); }
        foreach (string getter in new[] { "get_ViewWidth", "get_ViewHeight" })
        { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, window.Methods.Single(m => m.Name == getter)); }
        uint color = Convert.ToUInt32(window.Fields.Single(f => f.Name == "BgColor").Constant);
        if (color >> 24 != 255) throw new InvalidDataException("Viewport clear must remain opaque");
        il.Emit(OpCodes.Ldc_I4, unchecked((int)color));
        il.Emit(OpCodes.Call, module.ImportReference(typeof(SwitchRaster).GetMethod(nameof(SwitchRaster.TryFill))!));
        il.Emit(OpCodes.Ret); helper.Body.MaxStackSize = 8;
        var writer = compose.Body.GetILProcessor(); var original = begin.Next;
        writer.InsertBefore(original, writer.Create(OpCodes.Ldarg_0));
        writer.InsertBefore(original, writer.Create(OpCodes.Call, helper));
        writer.InsertBefore(original, writer.Create(OpCodes.Brtrue, target));
        log("PASS native opaque viewport clear; original managed loop retained as fallback");
    }
}
