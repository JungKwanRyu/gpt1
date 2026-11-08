using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Mono.Cecil;
using WogPatcher;

if (args.Length != 2) throw new ArgumentException("Usage: GameAudit release-directory report-directory");
string release = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var patchPhases = new List<string>();
WogShim.Host.LogSink = line => {
    string phase = line.StartsWith("[wog] ", StringComparison.Ordinal) ? line[6..] : line;
    if (phase.StartsWith("PATCH ", StringComparison.Ordinal)) patchPhases.Add(phase);
    Console.WriteLine(line);
};
RenameSemanticsSmoke.Run(output);
PortableFilesSmoke.Run(output);
RuntimeApiAudit.Run(output);
DiagnosticsSmoke.Run();
AudioSinkSmoke.Run();
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
try
{
    LoaderSmoke.Run(release, output);
    foreach (string phase in new[] { "Retarget", "RedirectCalls", "RewritePInvokes", "Verify" })
        foreach (string assembly in GamePatcher.GameAssemblies)
            if (!patchPhases.Any(p => p == "PATCH " + phase + ": " + assembly))
                throw new InvalidDataException("Missing real game patch progress: " + phase + " / " + assembly);
    Console.WriteLine("PASS actual patch progress callbacks cover four phases of both game DLLs");
}
catch (Exception ex)
{
    File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString());
    Console.WriteLine("FAIL REAL GAME LOADER AUDIT: " + ex);
    Environment.ExitCode = 1;
}
