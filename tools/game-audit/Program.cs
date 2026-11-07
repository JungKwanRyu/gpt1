using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Mono.Cecil;
using WogPatcher;

if (args.Length != 2) throw new ArgumentException("Usage: GameAudit release-directory report-directory");
string release = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
var api = new Dictionary<string, object>();
foreach (Type type in typeof(GameLayout).Assembly.GetTypes().Where(t => t.IsPublic))
    api[type.FullName!] = new { Fields = type.GetFields(flags).Select(f => f.ToString()).ToArray(),
        Methods = type.GetMethods(flags | BindingFlags.DeclaredOnly).Select(m => m.ToString()).ToArray() };
File.WriteAllText(Path.Combine(output, "patcher-api.json"), JsonSerializer.Serialize(api, new JsonSerializerOptions { WriteIndented = true }));
using (var patcher = AssemblyDefinition.ReadAssembly(typeof(GameLayout).Assembly.Location))
{
    var lines = new List<string>();
    foreach (var type in patcher.MainModule.Types)
        foreach (var method in type.Methods)
            if (method.HasBody && (type.Name == "GameLauncher" || (type.Name == "ReleaseFetcher" && method.Name is "Write" or "Wanted" or ".cctor")))
            {
                lines.Add(method.FullName);
                lines.AddRange(method.Body.Instructions.Select(i => i.ToString()));
            }
    File.WriteAllLines(Path.Combine(output, "patcher-launcher-il.txt"), lines);
}
Console.WriteLine("Game assemblies: " + string.Join(", ", GamePatcher.GameAssemblies));
Console.WriteLine("Release DLLs: " + string.Join(", ", Directory.GetFiles(release, "*.dll").Select(Path.GetFileName)));
string patched = Path.Combine(output, "patched");
var layout = new GameLayout(release, patched, Path.Combine(output, "save"), null, typeof(WogShim.Host).Assembly.Location);
try
{
    string prepared = GameLauncher.Prepare(layout, Console.WriteLine);
    var report = GameLauncher.LastReport;
    File.WriteAllText(Path.Combine(output, "patch-report.txt"), report?.ToString() ?? "No patch report");
    Console.WriteLine("Prepared " + prepared);
    var native = new Dictionary<string, object>();
    foreach (string file in GamePatcher.GameAssemblies)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(prepared, file));
        native[file] = new { Name = assembly.Name.FullName,
            References = assembly.MainModule.AssemblyReferences.Select(r => r.FullName).ToArray(),
            PInvokes = assembly.MainModule.GetTypes().SelectMany(t => t.Methods).Where(m => m.IsPInvokeImpl).Select(m => new { m.FullName, Library = m.PInvokeInfo?.Module.Name, Entry = m.PInvokeInfo?.EntryPoint }).ToArray() };
    }
    File.WriteAllText(Path.Combine(output, "patched-inventory.json"), JsonSerializer.Serialize(native, new JsonSerializerOptions { WriteIndented = true }));
    AssemblyLoadContext.Default.Resolving += (ctx, name) => {
        string p = Path.Combine(prepared, name.Name + ".dll");
        return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
    };
    foreach (string file in GamePatcher.GameAssemblies.Reverse())
    {
        var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(prepared, file));
        Console.WriteLine("Loaded " + asm.FullName);
        if (file == "WarOfGenesis.dll")
        {
            var main = asm.GetType("DuelDx.Program", true)!.GetMethod("Main", flags);
            if (main == null || !main.IsStatic) throw new MissingMethodException("DuelDx.Program.Main");
            Console.WriteLine("PASS real patched game Main lookup: " + main);
        }
    }
    Console.WriteLine("PASS REAL GAME PATCH AND LOAD; Main was not invoked");
}
catch (Exception ex)
{
    File.WriteAllText(Path.Combine(output, "patch-report.txt"), GameLauncher.LastReport?.ToString() ?? "No patch report");
    File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString());
    Console.WriteLine("FAIL REAL GAME AUDIT: " + ex);
    Environment.ExitCode = 1;
}
