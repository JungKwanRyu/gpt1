using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

if (args.Length != 2) throw new ArgumentException("Usage: RuntimeCompat WogPatcher.dll report.json");
string path = Path.GetFullPath(args[0]);
string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
int changed = 0;
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
    assembly.Write(path + ".net9-compatible");
}
File.Move(path + ".net9-compatible", path, true);
string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
var report = new { input = Path.GetFileName(path), original_sha256 = before, adapted_sha256 = after,
    reverse_array_calls_adapted = changed, change = "Enumerable.Reverse<T>(T[]) -> Enumerable.Reverse<T>(IEnumerable<T>)" };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("PASS SDK9 compatibility adaptation: " + changed + " array Reverse call; original=" + before + "; adapted=" + after);
