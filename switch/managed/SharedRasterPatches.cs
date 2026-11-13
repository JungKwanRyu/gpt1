using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WogSwitch;

internal static class SharedRasterPatches
{
    internal static void Apply(ModuleDefinition module, Action<string> log)
    {
        var window = module.GetType("DuelDx.GameWindow");
        foreach (string name in new[] { "DrawFrame", "FillRect", "BlitMasked" })
        {
            var core = window.Methods.Single(m => m.Name == name);
            string full = core.FullName;
            var callers = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions).Where(i => i.Operand is MethodReference r && r.FullName == full).ToArray();
            var wrapper = new MethodDefinition(name, core.Attributes, core.ReturnType);
            foreach (var p in core.Parameters) wrapper.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
            window.Methods.Add(wrapper);
            var il = wrapper.Body.GetILProcessor();
            var fallback = il.Create(OpCodes.Ldarg_0);
            if (name == "BlitMasked")
            {
                if (core.Parameters.Count != 10) throw new InvalidDataException("BlitMasked signature changed");
                il.Emit(OpCodes.Ldarg, wrapper.Parameters[9]); il.Emit(OpCodes.Brtrue, fallback);
            }
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, window.Fields.Single(f => f.Name == "_fb"));
            foreach (string getter in new[] { "get_BoardWidth", "get_BoardHeight" })
            { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, window.Methods.Single(m => m.Name == getter)); }
            if (name == "FillRect")
            {
                foreach (var p in wrapper.Parameters) il.Emit(OpCodes.Ldarg, p);
                il.Emit(OpCodes.Call, module.ImportReference(typeof(SwitchRaster).GetMethod(nameof(SwitchRaster.TryFill))!));
            }
            else if (name == "DrawFrame")
            {
                if (core.Parameters.Count != 5) throw new InvalidDataException("DrawFrame signature changed");
                void Frame(string property)
                {
                    var getter = core.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>()
                        .First(m => m.Name == "get_" + property && m.DeclaringType.FullName == core.Parameters[0].ParameterType.FullName);
                    il.Emit(core.Parameters[0].ParameterType.IsValueType ? OpCodes.Ldarga : OpCodes.Ldarg, wrapper.Parameters[0]);
                    il.Emit(OpCodes.Call, getter);
                }
                Frame("Px"); Frame("W"); Frame("H");
                il.Emit(OpCodes.Ldarg, wrapper.Parameters[1]); Frame("X"); il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldarg, wrapper.Parameters[2]); Frame("Y"); il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldarg, wrapper.Parameters[3]); il.Emit(OpCodes.Ldarg, wrapper.Parameters[4]);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, window.Fields.Single(f => f.Name == "_uiClip"));
                il.Emit(OpCodes.Call, module.ImportReference(typeof(SwitchRaster).GetMethod(nameof(SwitchRaster.TryFrame))!));
            }
            else
            {
                foreach (int at in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }) il.Emit(OpCodes.Ldarg, wrapper.Parameters[at]);
                il.Emit(OpCodes.Call, module.ImportReference(typeof(BattleRaster).GetMethod(nameof(BattleRaster.TryMasked))!));
            }
            il.Emit(OpCodes.Brfalse, fallback); il.Emit(OpCodes.Ret);
            il.Append(fallback);
            foreach (var p in wrapper.Parameters) il.Emit(OpCodes.Ldarg, p);
            core.Name = "SwitchOriginal" + name;
            il.Emit(OpCodes.Call, core); il.Emit(OpCodes.Ret); wrapper.Body.MaxStackSize = 16;
            foreach (var caller in callers) caller.Operand = wrapper;
            log("PASS shared native raster route with original fallback: " + name + "; call sites=" + callers.Length);
        }
    }
}
