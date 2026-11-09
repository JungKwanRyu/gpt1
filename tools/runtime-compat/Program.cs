using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2) throw new ArgumentException("Usage: RuntimeCompat WogPatcher.dll report.json");
string path = Path.GetFullPath(args[0]);
string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
// The pinned shim uses the .NET 10 BCL InlineArray5<string> in HlslBlob.Pack.
// Emit the same five NUL-separated fields using the long-standing array Join
// overload. No custom inline-array layout or .NET 10 type is needed on Mono9.
string shimPath = Path.Combine(Path.GetDirectoryName(path)!, "WogShim.dll");
string shimBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(shimPath))).ToLowerInvariant();
using (var shim = AssemblyDefinition.ReadAssembly(shimPath, new ReaderParameters { InMemory = true }))
{
    var module = shim.MainModule;
    var pack = module.GetType("WogShim.Gpu.HlslBlob").Methods.Single(m => m.Name == "Pack");
    if (!pack.IsStatic || pack.Parameters.Count != 4 || pack.Parameters.Any(p => p.ParameterType.FullName != "System.String") ||
        pack.ReturnType.FullName != "System.Byte[]" ||
        pack.Body.Variables.Count != 1 || !pack.Body.Variables[0].VariableType.FullName.Contains("System.Runtime.CompilerServices.InlineArray5`1") ||
        pack.Body.Instructions.Count(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, "WOGHLSL")) != 1)
        throw new InvalidDataException("Unexpected pinned HlslBlob.Pack layout");
    var utf8 = (MethodReference)pack.Body.Instructions.First().Operand;
    var bytes = (MethodReference)pack.Body.Instructions.Last(i => i.OpCode == OpCodes.Callvirt).Operand;
    pack.Body = new Mono.Cecil.Cil.MethodBody(pack) { MaxStackSize = 7 };
    var il = pack.Body.GetILProcessor();
    il.Emit(OpCodes.Call, utf8);
    il.Emit(OpCodes.Ldstr, "\0");
    il.Emit(OpCodes.Ldc_I4_5); il.Emit(OpCodes.Newarr, module.TypeSystem.String);
    for (int i = 0; i < 5; i++)
    {
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4, i);
        if (i == 0) il.Emit(OpCodes.Ldstr, "WOGHLSL");
        else il.Emit(OpCodes.Ldarg, pack.Parameters[new[] { 3, 1, 2, 0 }[i - 1]]);
        il.Emit(OpCodes.Stelem_Ref);
    }
    il.Emit(OpCodes.Call, module.ImportReference(typeof(string).GetMethod("Join", new[] { typeof(string), typeof(string[]) })!));
    il.Emit(OpCodes.Callvirt, bytes); il.Emit(OpCodes.Ret);
    shim.Write(shimPath + ".net9-compatible");
}
File.Move(shimPath + ".net9-compatible", shimPath, true);
string shimAfter = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(shimPath))).ToLowerInvariant();
int changed = 0;
var progressMethods = new List<string>();
using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true }))
{
    foreach (var type in assembly.MainModule.GetTypes())
        foreach (var method in type.Methods.Where(m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not GenericInstanceMethod instance) continue;
                var target = instance.ElementMethod;
                if (target.DeclaringType.FullName != "System.Linq.Enumerable" || target.Name != "Reverse" ||
                    target.Parameters.Count != 1 || target.Parameters[0].ParameterType is not ArrayType) continue;
                if (target.GenericParameters.Count != 1 || target.ReturnType is not GenericInstanceType result ||
                    result.ElementType.FullName != "System.Collections.Generic.IEnumerable`1")
                    throw new InvalidDataException("Unexpected Enumerable.Reverse array signature");
                // A T[] already implements IEnumerable<T>; only the MemberRef
                // signature changes. The classic overload exists in .NET 9.
                target.Parameters[0].ParameterType = target.ReturnType;
                changed++;
            }
    if (changed != 1) throw new InvalidDataException("Expected exactly one original .NET 10 array Reverse call; found " + changed);
    // Preserve patch logic and expose its major phases through the existing
    // WogShim host log callback; no per-type/per-method logging overhead.
    var module = assembly.MainModule;
    using var shim = AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(path)!, "WogShim.dll"));
    var hostLog = module.ImportReference(shim.MainModule.GetType("WogShim.Host").Methods.Single(m =>
        m.Name == "Log" && m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.String"));
    var nameGetter = module.ImportReference(typeof(ModuleDefinition).GetProperty("Name")!.GetMethod!);
    var concat = module.ImportReference(typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) })!);
    var patcher = module.GetType("WogPatcher.GamePatcher");
    foreach (string name in new[] { "Patch", "Retarget", "RedirectCalls", "RewritePInvokes", "Verify" })
    {
        var method = patcher.Methods.Single(m => m.Name == name);
        if (!method.IsStatic || !method.HasBody || (name != "Patch" && method.Parameters[0].ParameterType.FullName != "Mono.Cecil.ModuleDefinition"))
            throw new InvalidDataException("Unexpected patcher phase signature: " + name);
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldstr, "PATCH " + name + (name == "Patch" ? " begin" : ": ")));
        if (name != "Patch")
        {
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Callvirt, nameGetter));
            il.InsertBefore(first, il.Create(OpCodes.Call, concat));
        }
        il.InsertBefore(first, il.Create(OpCodes.Call, hostLog));
        // Cecil does not calculate maxstack when reading the rewritten IL.
        method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 2);
        progressMethods.Add(name);
    }
    assembly.Write(path + ".net9-compatible");
}
File.Move(path + ".net9-compatible", path, true);
string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
var report = new { input = Path.GetFileName(path), original_sha256 = before, adapted_sha256 = after,
    reverse_array_calls_adapted = changed, progress_methods = progressMethods,
    shim_original_sha256 = shimBefore, shim_adapted_sha256 = shimAfter,
    shader_blob = "HlslBlob.Pack: same UTF8 NUL-separated WOGHLSL/name/entry/profile/source fields using String.Join(string,string[]) instead of BCL10 InlineArray5",
    change = "SDK9 shader blob, Reverse signature and major patch phases through WogShim.Host.Log" };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("PASS SDK9 compatibility adaptation: " + changed + " array Reverse call; original=" + before + "; adapted=" + after);
