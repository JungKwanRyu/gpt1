using System.Reflection;
using System.Runtime.ExceptionServices;
using Mono.Cecil;
using WogPatcher;

namespace WogSwitch;

internal static class PatchValidation
{
    internal static PatchReport Verify(string directory, string shimPath, Action<string> log)
    {
        var verify = typeof(GamePatcher).GetMethod("Verify", BindingFlags.NonPublic | BindingFlags.Static, null,
            new[] { typeof(ModuleDefinition), typeof(ModuleDefinition), typeof(PatchReport) }, null);
        if (verify == null || verify.ReturnType != typeof(void)) throw new MissingMethodException("Pinned GamePatcher.Verify signature changed");
        using var resolver = new DefaultAssemblyResolver();
        foreach (string path in new[] { directory, Path.GetDirectoryName(shimPath)!, Path.GetDirectoryName(typeof(PlatformGameBridge).Assembly.Location)! })
            resolver.AddSearchDirectory(path);
        var parameters = new ReaderParameters { AssemblyResolver = resolver, InMemory = true };
        using var shim = AssemblyDefinition.ReadAssembly(shimPath, parameters);
        var report = new PatchReport();
        foreach (string name in GamePatcher.GameAssemblies)
        {
            Diagnostics.Step("read-only original Verify " + name, log);
            using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(directory, name), parameters);
            // The public Patch pass reports unsupported native imports in its
            // RewritePInvokes phase, outside Verify. A prepared Switch image
            // must have no direct P/Invoke left, even if a shim could map it.
            foreach (var method in assembly.MainModule.GetTypes().SelectMany(t => t.Methods).Where(m => m.IsPInvokeImpl))
                report.Missing.Add("P/Invoke without shim: " + method.PInvokeInfo.Module.Name + "!" + method.PInvokeInfo.EntryPoint +
                    " (" + method.DeclaringType.FullName + "." + method.Name + ")");
            try { verify.Invoke(null, new object[] { assembly.MainModule, shim.MainModule, report }); }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        }
        return report;
    }
}
