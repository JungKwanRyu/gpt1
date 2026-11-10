using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WogSwitch;

internal static class FieldRasterPatches
{
    internal static void Apply(ModuleDefinition module, Action<string> log)
    {
        var window = module.GetType("DuelDx.GameWindow");
        var field = module.GetType("DuelDx.FieldScene");
        var host = field.Fields.Single(f => f.FieldType == window);
        FieldDefinition F(string name) => field.Fields.Single(f => f.Name == name);
        MethodDefinition Wrap(string name)
        {
            var original = field.Methods.Single(m => m.Name == name);
            var callers = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions).Where(i => i.Operand is MethodReference r && r.FullName == original.FullName).ToArray();
            var wrapper = new MethodDefinition(name, original.Attributes, original.ReturnType);
            foreach (var p in original.Parameters) wrapper.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
            original.Name = "SwitchOriginal" + name;
            field.Methods.Add(wrapper);
            foreach (var call in callers) call.Operand = wrapper;
            wrapper.Body.MaxStackSize = 16;
            return wrapper;
        }
        var backdrop = Wrap("DrawFieldBackdrop");
        if (backdrop.Parameters.Count != 2) throw new InvalidDataException("Field backdrop signature changed");
        var il = backdrop.Body.GetILProcessor();
        void Host(ILProcessor writer) { writer.Emit(OpCodes.Ldarg_0); writer.Emit(OpCodes.Ldfld, host); }
        Host(il); il.Emit(OpCodes.Ldfld, window.Fields.Single(f => f.Name == "_fb"));
        foreach (string name in new[] { "get_BoardWidth", "get_BoardHeight" })
        { Host(il); il.Emit(OpCodes.Call, window.Methods.Single(m => m.Name == name)); }
        foreach (string name in new[] { "_fieldBg", "_fieldCam", "_fieldPieces" })
        { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, F(name)); }
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
        Host(il); il.Emit(OpCodes.Ldfld, window.Fields.Single(f => f.Name == "_lastTime"));
        il.Emit(OpCodes.Ldc_R8, Convert.ToDouble(window.Fields.Single(f => f.Name == "TicksPerSecond").Constant));
        il.Emit(OpCodes.Mul); il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(SwitchRaster).GetMethod(nameof(SwitchRaster.TryFieldBackdrop))!));
        var fallback = il.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, fallback); il.Emit(OpCodes.Ret); il.Append(fallback);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, field.Methods.Single(m => m.Name == "SwitchOriginalDrawFieldBackdrop")); il.Emit(OpCodes.Ret);

        // Retain the original method for pixel comparison and route every caller
        // through the equivalent opaque blit + unchanged layer renderer.
        var scene = Wrap("DrawFieldScene");
        if (scene.Parameters.Count != 3 || scene.ReturnType.MetadataType != MetadataType.Int32)
            throw new InvalidDataException("Field scene signature changed");
        var start = new VariableDefinition(module.TypeSystem.Int32); scene.Body.Variables.Add(start); scene.Body.InitLocals = true;
        var writer = scene.Body.GetILProcessor();
        var noPicture = writer.Create(OpCodes.Ldarg_0); var layers = writer.Create(OpCodes.Ldarg_0);
        writer.Emit(OpCodes.Ldarg_0); writer.Emit(OpCodes.Ldfld, F("_fieldPicture")); writer.Emit(OpCodes.Brfalse, noPicture);
        Host(writer); writer.Emit(OpCodes.Ldarg_0); writer.Emit(OpCodes.Ldfld, F("_fieldPicture"));
        writer.Emit(OpCodes.Ldarg_1); writer.Emit(OpCodes.Ldarg_2);
        writer.Emit(OpCodes.Call, window.Methods.Single(m => m.Name == "SwitchOpaqueBackdrop"));
        writer.Emit(OpCodes.Ldarg_0); writer.Emit(OpCodes.Ldfld, F("_fieldPictureCover")); writer.Emit(OpCodes.Stloc, start);
        writer.Emit(OpCodes.Br, layers); writer.Append(noPicture);
        writer.Emit(OpCodes.Ldarg_1); writer.Emit(OpCodes.Ldarg_2); writer.Emit(OpCodes.Call, backdrop);
        writer.Append(layers); writer.Emit(OpCodes.Ldarg_1); writer.Emit(OpCodes.Ldarg_2);
        writer.Emit(OpCodes.Ldloc, start); writer.Emit(OpCodes.Ldarg_3);
        writer.Emit(OpCodes.Call, field.Methods.Single(m => m.Name == "DrawFieldLayers"));
        writer.Emit(OpCodes.Ldloc, start); writer.Emit(OpCodes.Ret);
        log("PASS field color-key/parallax backdrop and retained-picture native routes; original methods preserved");
    }
}
