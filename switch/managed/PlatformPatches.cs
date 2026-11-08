using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WogSwitch;

internal static class PlatformPatches
{
    internal const string Version = "switch-platform-0.5.3-v1";

    internal static void Apply(string directory, Action<string> log)
    {
        string path = Path.Combine(directory, "WarOfGenesis.dll");
        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true }))
        {
            var module = assembly.MainModule;
            ReplaceReportUi(module, "DuelDx.MyBugReportsDialog", new[] { "ShotIfAsked", "Show", "StatusName" });
            ReplaceReportUi(module, "DuelDx.BugReportDialog", new[] { "Ask" });
            var report = module.GetType("DuelDx.BugReport") ?? throw new InvalidDataException("BugReport type changed");
            var open = report.Methods.Single(m => m.Name == "Open" && !m.IsStatic && m.Parameters.Count == 0);
            SetBody(open, il => { il.Emit(OpCodes.Call, Import(module, nameof(PlatformGameBridge.ReportUnavailable))); il.Emit(OpCodes.Ret); });
            RewritePowerAndDpi(module, log);
            var crash = module.GetType("DuelDx.Program")?.Methods.SingleOrDefault(m => m.Name == "ReportCrash" && m.Parameters.Count == 1);
            if (crash == null || crash.Parameters[0].ParameterType.FullName != "System.Exception")
                throw new InvalidDataException("Program.ReportCrash signature changed");
            SetBody(crash, il => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, Import(module, nameof(PlatformGameBridge.Crash))); il.Emit(OpCodes.Ret); });
            RewriteHostCalls(module);
            WriteAndReplace(assembly, path);
        }
        path = Path.Combine(directory, "WarOfGenesis.Assets.dll");
        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true }))
        {
            foreach (var item in new[] { (Type: "WarOfGenesis.Assets.OriginalGame", Bridge: nameof(PlatformGameBridge.ScanPart2)),
                         (Type: "WarOfGenesis.Assets.Part1Game", Bridge: nameof(PlatformGameBridge.ScanPart1)) })
            {
                var scan = assembly.MainModule.GetType(item.Type)?.Methods.SingleOrDefault(m => m.Name == "Scan" && m.IsStatic);
                if (scan == null || scan.ReturnType.FullName != "System.String") throw new InvalidDataException("Original folder scan signature changed: " + item.Type);
                SetBody(scan, il => { il.Emit(OpCodes.Call, Import(assembly.MainModule, item.Bridge)); il.Emit(OpCodes.Ret); });
            }
            RewriteHostCalls(assembly.MainModule);
            WriteAndReplace(assembly, path);
        }
        log("PASS Switch platform patches: report UI notice, power/DPI calls, configured scans, app paths, controlled exit/crash");
    }

    private static MethodReference Import(ModuleDefinition module, string name) => module.ImportReference(
        typeof(PlatformGameBridge).GetMethod(name) ?? throw new MissingMethodException(name));

    private static void SetBody(MethodDefinition method, Action<ILProcessor> write)
    {
        if (!method.HasBody) throw new InvalidDataException("Expected managed method body: " + method.FullName);
        method.Body = new MethodBody(method);
        write(method.Body.GetILProcessor());
        method.DebugInformation.SequencePoints.Clear();
        method.DebugInformation.Scope = null;
    }

    private static void ReplaceReportUi(ModuleDefinition module, string name, string[] keep)
    {
        var type = module.GetType(name) ?? throw new InvalidDataException("Report UI type changed: " + name);
        var removed = type.Methods.Where(m => !keep.Contains(m.Name)).ToHashSet();
        var removedTypes = type.NestedTypes.SelectMany(Descendants).Select(t => t.FullName).ToHashSet();
        foreach (var other in module.GetTypes().Where(t => t != type && !removedTypes.Contains(t.FullName)))
            foreach (var method in other.Methods.Where(m => m.HasBody))
                foreach (var instruction in method.Body.Instructions)
                    if (instruction.Operand is MethodReference reference && reference.DeclaringType.FullName == name &&
                        removed.Any(m => m.FullName == reference.FullName))
                        throw new InvalidDataException("Report UI helper is referenced outside its dialog: " + reference.FullName);
        foreach (var method in type.Methods.Where(m => m.Name != "StatusName" && keep.Contains(m.Name)))
            SetBody(method, il => {
                if (method.Name == "ShotIfAsked") il.Emit(OpCodes.Ldc_I4_0);
                else
                {
                    il.Emit(OpCodes.Call, Import(module, nameof(PlatformGameBridge.ReportUnavailable)));
                    if (method.ReturnType.FullName == "System.String") il.Emit(OpCodes.Ldnull);
                    else if (method.ReturnType.FullName != "System.Void") throw new InvalidDataException("Report UI entry signature changed");
                }
                il.Emit(OpCodes.Ret);
            });
        foreach (var method in removed) type.Methods.Remove(method);
        type.Fields.Clear();
        type.NestedTypes.Clear();
    }

    private static IEnumerable<TypeDefinition> Descendants(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(Descendants)) yield return nested;
    }

    private static void RewritePowerAndDpi(ModuleDefinition module, Action<string> log)
    {
        var victims = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.IsPInvokeImpl &&
            ((m.PInvokeInfo?.Module.Name.Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase) == true && m.PInvokeInfo.EntryPoint == "SetThreadExecutionState") ||
             (m.PInvokeInfo?.Module.Name.Equals("user32.dll", StringComparison.OrdinalIgnoreCase) == true && m.PInvokeInfo.EntryPoint == "GetDpiForWindow"))).ToArray();
        int sites = 0;
        foreach (var method in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (var instruction in method.Body.Instructions.ToArray())
            {
                if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference reference) continue;
                var victim = victims.FirstOrDefault(v => v.FullName == reference.FullName);
                if (victim == null) continue;
                if (!victim.IsStatic || victim.Parameters.Count != 1 || victim.ReturnType.FullName != "System.UInt32")
                    throw new InvalidDataException("Windows power/DPI function signature changed");
                var il = method.Body.GetILProcessor();
                instruction.OpCode = OpCodes.Pop; instruction.Operand = null;
                il.InsertAfter(instruction, il.Create(OpCodes.Ldc_I4, victim.Name == "GetDpiForWindow" ? 96 : 0));
                sites++;
            }
        foreach (var victim in victims)
        {
            if (module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                .Any(i => i.Operand is MethodReference r && r.FullName == victim.FullName))
                throw new InvalidDataException("Unrewritten reference to Windows-only function: " + victim.FullName);
            victim.DeclaringType.Methods.Remove(victim);
        }
        log("Windows power/DPI call sites replaced: " + sites);
    }

    private static void RewriteHostCalls(ModuleDefinition module)
    {
        foreach (var method in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Call && instruction.Operand is GenericInstanceMethod generic &&
                    generic.ElementMethod.DeclaringType.FullName == "System.Linq.Enumerable" && generic.Name == "Reverse" &&
                    generic.ElementMethod.Parameters.Count == 1 && generic.ElementMethod.Parameters[0].ParameterType is ArrayType)
                    generic.ElementMethod.Parameters[0].ParameterType = generic.ElementMethod.ReturnType;
                if (instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference fileCall &&
                    fileCall.DeclaringType.FullName == "System.IO.File" && fileCall.Name is "Copy" or "Move")
                {
                    if (fileCall.Parameters.Count is not (2 or 3) ||
                        fileCall.Parameters[0].ParameterType.FullName != "System.String" || fileCall.Parameters[1].ParameterType.FullName != "System.String" ||
                        (fileCall.Parameters.Count == 3 && fileCall.Parameters[2].ParameterType.FullName != "System.Boolean"))
                        throw new InvalidDataException("Unexpected File.Copy/Move signature: " + fileCall.FullName);
                    var parameters = fileCall.Parameters.Count == 2 ? new[] { typeof(string), typeof(string) } : new[] { typeof(string), typeof(string), typeof(bool) };
                    instruction.Operand = module.ImportReference(typeof(PlatformGameBridge).GetMethod(fileCall.Name == "Copy" ? "CopyFile" : "MoveFile", parameters)!);
                }
                if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference reference || reference.DeclaringType.FullName != "System.Environment") continue;
                if (reference.Name == "Exit" && reference.Parameters.Count == 1 && reference.Parameters[0].ParameterType.FullName == "System.Int32")
                    instruction.Operand = Import(module, nameof(PlatformGameBridge.Exit));
                else if (reference.Name == "GetFolderPath" && reference.Parameters.Count == 1)
                    instruction.Operand = Import(module, nameof(PlatformGameBridge.GetFolderPath));
            }
    }

    private static void WriteAndReplace(AssemblyDefinition assembly, string path)
    {
        // Horizon's rename rejects an existing destination even when .NET
        // requests overwrite. Serialize completely before opening the owned
        // patch-cache file with create/truncate; never replace a release input.
        // The cache completion marker/hashes are written only after all patches
        // and validation finish, so an interrupted write cannot be reused.
        using var image = new MemoryStream();
        assembly.Write(image);
        image.Position = 0;
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        image.CopyTo(output);
    }
}
