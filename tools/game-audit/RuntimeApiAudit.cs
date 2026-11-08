using Mono.Cecil;
using WogPatcher;

internal static class RuntimeApiAudit
{
    internal static void Run(string output)
    {
        string? runtime = Environment.GetEnvironmentVariable("WOG_SDK_MONO");
        if (string.IsNullOrEmpty(runtime)) return;
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(runtime);
        using var patcher = AssemblyDefinition.ReadAssembly(typeof(GamePatcher).Assembly.Location, new ReaderParameters { AssemblyResolver = resolver });
        var lines = new List<string>();
        var problems = new HashSet<string>();
        foreach (var type in patcher.MainModule.GetTypes())
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
        File.WriteAllLines(Path.Combine(output, "patcher-static-initializer.txt"), lines);
        File.WriteAllLines(Path.Combine(output, "sdk9-patcher-unresolved-methods.txt"), problems);
        Console.WriteLine("SDK9 PATCHER API AUDIT unresolved=" + problems.Count);
        foreach (string problem in problems) Console.WriteLine("SDK9 API: " + problem);
    }
}
