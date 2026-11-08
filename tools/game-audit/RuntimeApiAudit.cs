using Mono.Cecil;
using WogPatcher;

internal static class RuntimeApiAudit
{
    internal static void Run(string output)
    {
        string? runtime = Environment.GetEnvironmentVariable("WOG_SDK_MONO");
        if (string.IsNullOrEmpty(runtime)) return;
        var inputs = new[] {
            (Name: "patcher", Path: typeof(GamePatcher).Assembly.Location),
            (Name: "shim", Path: typeof(WogShim.Host).Assembly.Location),
            (Name: "managed-host", Path: System.IO.Path.GetFullPath("work/managed-api-audit/WogSwitch.Managed.dll")) };
        int total = 0;
        foreach (var input in inputs)
        {
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(runtime);
            using var assembly = AssemblyDefinition.ReadAssembly(input.Path, new ReaderParameters { AssemblyResolver = resolver });
            var lines = new List<string>();
            var problems = new HashSet<string>();
            foreach (var type in assembly.MainModule.GetTypes())
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    if (type.Name == "GamePatcher" && method.Name == ".cctor")
                    {
                        lines.Add(method.FullName);
                        lines.AddRange(method.Body.Instructions.Select(i => i.ToString()));
                    }
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is not MethodReference reference || reference.DeclaringType.Scope is not AssemblyNameReference scope || !scope.Name.StartsWith("System.")) continue;
                        try { if (reference.Resolve() == null) problems.Add(method.FullName + " -> " + reference.FullName); }
                        catch (Exception ex) { problems.Add(method.FullName + " -> " + reference.FullName + " [" + ex.GetType().Name + "]"); }
                    }
                }
            if (lines.Count != 0) File.WriteAllLines(System.IO.Path.Combine(output, "patcher-static-initializer.txt"), lines);
            File.WriteAllLines(System.IO.Path.Combine(output, "sdk9-" + input.Name + "-unresolved-methods.txt"), problems);
            Console.WriteLine("SDK9 " + input.Name + " API AUDIT unresolved=" + problems.Count);
            foreach (string problem in problems) Console.WriteLine("SDK9 API: " + problem);
            total += problems.Count;
        }
        if (total != 0) throw new InvalidDataException("SDK9 API inspection found unresolved calls: " + total);
    }
}
