using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WogSwitch;

internal static class SceneRasterPatches
{
    internal static void Apply(ModuleDefinition module, Action<string> log)
    {
        var window = module.GetType("DuelDx.GameWindow");
        var helper = new MethodDefinition("SwitchOpaqueBackdrop", MethodAttributes.Assembly, module.TypeSystem.Void);
        helper.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.None, new ArrayType(module.TypeSystem.UInt32)));
        helper.Parameters.Add(new ParameterDefinition("x", ParameterAttributes.None, module.TypeSystem.Int32));
        helper.Parameters.Add(new ParameterDefinition("y", ParameterAttributes.None, module.TypeSystem.Int32));
        window.Methods.Add(helper);
        var il = helper.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, window.Fields.Single(f => f.Name == "_fb"));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, window.Methods.Single(m => m.Name == "get_BoardWidth"));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, window.Methods.Single(m => m.Name == "get_BoardHeight"));
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, 640); il.Emit(OpCodes.Ldc_I4, 480);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(SwitchRaster).GetMethod(nameof(SwitchRaster.BlitOpaque))!));
        il.Emit(OpCodes.Ret); helper.Body.MaxStackSize = 8;

        // Limit replacement to the verified opaque 640x480 backdrop loop.
        // Keep each screen's clearing, animation, menus and text unchanged.
        foreach (var target in new[] { ("DuelDx.TitleScreen", "DrawTitle"), ("DuelDx.EpisodesScreen", "DrawEpisodes"), ("DuelDx.Part1TitleScreen", "Draw") })
        {
            var type = module.GetType(target.Item1);
            var draw = type.Methods.Single(m => m.Name == target.Item2);
            var code = draw.Body.Instructions;
            var fill = code.Single(i => i.Operand is MethodReference m && m.DeclaringType == window && m.Name == "FillRect");
            var pixel = code.Single(i => i.Operand is MethodReference m && m.DeclaringType == window && m.Name == "SetPixel");
            var bg = code.Skip(code.IndexOf(fill) + 1).Take(code.IndexOf(pixel) - code.IndexOf(fill) - 1)
                .Single(i => i.Operand is FieldReference f && f.FieldType.FullName == "System.UInt32[]");
            var branch = code.Skip(code.IndexOf(bg) + 1).Take(code.IndexOf(pixel) - code.IndexOf(bg) - 1)
                .First(i => i.OpCode == OpCodes.Brfalse || i.OpCode == OpCodes.Brfalse_S);
            var end = (Instruction)branch.Operand;
            int a = code.IndexOf(fill) + 1, b = code.IndexOf(end);
            if (b <= code.IndexOf(pixel) || draw.Body.ExceptionHandlers.Count != 0)
                throw new InvalidDataException("Unexpected backdrop control flow: " + draw.FullName);
            var removed = code.Skip(a).Take(b - a).ToHashSet();
            foreach (var instruction in code.Where(i => !removed.Contains(i)))
                if (instruction.Operand is Instruction jump && removed.Contains(jump) ||
                    instruction.Operand is Instruction[] jumps && jumps.Any(removed.Contains))
                    throw new InvalidDataException("External branch enters opaque backdrop loop");
            var before = code.Take(code.IndexOf(fill)).ToArray();
            VariableDefinition Coordinate(string name)
            {
                int at = Array.FindLastIndex(before, i => i.Operand is FieldReference f && f.Name == name && f.DeclaringType.FullName.StartsWith("System.ValueTuple`2"));
                if (at < 0) throw new InvalidDataException("Backdrop origin deconstruction changed");
                var store = before[at + 1];
                var variable = store.OpCode.Code switch {
                    Code.Stloc_0 => draw.Body.Variables[0], Code.Stloc_1 => draw.Body.Variables[1],
                    Code.Stloc_2 => draw.Body.Variables[2], Code.Stloc_3 => draw.Body.Variables[3],
                    Code.Stloc or Code.Stloc_S => (VariableDefinition)store.Operand,
                    _ => throw new InvalidDataException("Backdrop coordinate store changed") };
                if (variable.VariableType.MetadataType != MetadataType.Int32) throw new InvalidDataException("Backdrop coordinate type changed");
                return variable;
            }
            var x = Coordinate("Item1"); var y = Coordinate("Item2");
            // Capture the exact original expression producing the source array
            // (host.Mos._mosesBg or this._episodesBg), without its branch/loop.
            var expression = code.Skip(a).Take(code.IndexOf(bg) - a + 1).ToArray();
            if (expression.Any(i => i.OpCode.FlowControl != FlowControl.Next && i.OpCode.FlowControl != FlowControl.Call))
                throw new InvalidDataException("Backdrop source expression changed");
            var writer = draw.Body.GetILProcessor();
            var host = type.Fields.Single(f => f.FieldType == window);
            writer.InsertBefore(end, writer.Create(OpCodes.Ldarg_0));
            writer.InsertBefore(end, writer.Create(OpCodes.Ldfld, host));
            foreach (var instruction in expression)
            {
                var copy = writer.Create(OpCodes.Nop); copy.OpCode = instruction.OpCode; copy.Operand = instruction.Operand;
                writer.InsertBefore(end, copy);
            }
            writer.InsertBefore(end, writer.Create(OpCodes.Ldloc, x)); writer.InsertBefore(end, writer.Create(OpCodes.Ldloc, y));
            writer.InsertBefore(end, writer.Create(OpCodes.Call, helper));
            foreach (var instruction in removed) writer.Remove(instruction);
            draw.Body.MaxStackSize = Math.Max(draw.Body.MaxStackSize, 5);
            log("PASS native opaque backdrop replaces per-pixel loop: " + draw.FullName);
        }

        var render = window.Methods.Single(m => m.Name == "Render");
        var writer2 = render.Body.GetILProcessor();
        foreach (var call in render.Body.Instructions.Where(i => i.Operand is MethodReference m && m.DeclaringType == window && m.Name is "Compose" or "Upload" or "Draw").ToArray())
        {
            var method = (MethodReference)call.Operand;
            int phase = method.Name == "Compose" ? 0 : method.Name == "Upload" ? 1 : 2;
            if (method.ReturnType.MetadataType != MetadataType.Void || method.Parameters.Count != 0) throw new InvalidDataException("Render phase signature changed");
            var opcode = call.OpCode;
            call.OpCode = OpCodes.Ldc_I4; call.Operand = phase;
            var begin = writer2.Create(OpCodes.Call, module.ImportReference(typeof(FrameProfiler).GetMethod(nameof(FrameProfiler.Begin))!));
            var original = writer2.Create(opcode, method);
            var number = writer2.Create(OpCodes.Ldc_I4, phase);
            var finish = writer2.Create(OpCodes.Call, module.ImportReference(typeof(FrameProfiler).GetMethod(nameof(FrameProfiler.End))!));
            writer2.InsertAfter(call, begin); writer2.InsertAfter(begin, original); writer2.InsertAfter(original, number); writer2.InsertAfter(number, finish);
        }
        render.Body.MaxStackSize++;
    }
}
