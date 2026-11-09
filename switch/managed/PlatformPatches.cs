using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WogSwitch;

internal static class PlatformPatches
{
    internal const string Version = "switch-platform-0.5.9-v1";

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
            LocalizeInlineArray4(module, log);
            SceneRasterPatches.Apply(module, log);
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
            AddAssetDiagnostics(assembly.MainModule);
            WriteAndReplace(assembly, path);
        }
        log("PASS Switch platform patches: report UI notice, power/DPI calls, configured scans, app paths, controlled exit/crash");
    }

    private static void LocalizeInlineArray4(ModuleDefinition module, Action<string> log)
    {
        // C# targeting .NET 10 uses BCL InlineArray4<T>; the .NET 9 compiler
        // normally embeds this exact shape instead. Mono9 supports the
        // InlineArrayAttribute introduced in .NET 8, so localize the buffer.
        var references = module.GetTypeReferences().Where(t =>
            t.FullName == "System.Runtime.CompilerServices.InlineArray4`1").ToArray();
        if (references.Length != 1) throw new InvalidDataException("Expected one BCL10 InlineArray4 reference; found " + references.Length);
        var local = new TypeDefinition("WogSwitch.Compatibility", "InlineArray4`1",
            TypeAttributes.NotPublic | TypeAttributes.SequentialLayout | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            module.ImportReference(typeof(ValueType)));
        var element = new GenericParameter("T", local);
        local.GenericParameters.Add(element);
        local.Fields.Add(new FieldDefinition("_element0", FieldAttributes.Private, element));
        var attribute = new CustomAttribute(module.ImportReference(typeof(System.Runtime.CompilerServices.InlineArrayAttribute).GetConstructor(new[] { typeof(int) })!));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.Int32, 4));
        local.CustomAttributes.Add(attribute);
        module.Types.Add(local);
        foreach (var reference in references)
        {
            reference.Scope = module;
            reference.Namespace = local.Namespace;
            reference.Name = local.Name;
        }
        log("PASS SDK9 module-local InlineArray4<T> layout replaces the BCL10 type reference");
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
                if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference reference ||
                    reference.DeclaringType.FullName is not ("System.Environment" or "WogShim.BclRedirects")) continue;
                if (reference.Name == "Exit" && reference.Parameters.Count == 1 && reference.Parameters[0].ParameterType.FullName == "System.Int32")
                    instruction.Operand = Import(module, nameof(PlatformGameBridge.Exit));
                else if (reference.Name == "GetFolderPath" && reference.Parameters.Count == 1)
                    instruction.Operand = Import(module, nameof(PlatformGameBridge.GetFolderPath));
            }
    }

    private static void AddAssetDiagnostics(ModuleDefinition module)
    {
        var type = module.GetType("WarOfGenesis.Assets.OriginalAssets") ?? throw new InvalidDataException("OriginalAssets type changed");
        var prepare = type.Methods.Single(m => m.Name == "Prepare");
        prepare.Body.GetILProcessor().InsertBefore(prepare.Body.Instructions[0],
            Instruction.Create(OpCodes.Call, Import(module, nameof(PlatformGameBridge.BeginAssetPreparation))));
        foreach (var instruction in prepare.Body.Instructions)
            if (instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference file && file.DeclaringType.FullName == "System.IO.File")
            {
                if (file.Name == "Exists") instruction.Operand = Import(module, nameof(PlatformGameBridge.PreparedAssetExists));
                else if (file.Name == "Delete") instruction.Operand = Import(module, nameof(PlatformGameBridge.DeletePreparedAsset));
            }
        var find = module.GetType("WarOfGenesis.Assets.AssetsFolder").Methods.Single(m => m.Name == "Find");
        var findIl = find.Body.GetILProcessor();
        foreach (var ret in find.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
        {
            ret.OpCode = OpCodes.Call;
            ret.Operand = Import(module, nameof(PlatformGameBridge.ResolveAssetPath));
            findIl.InsertAfter(ret, findIl.Create(OpCodes.Ret));
        }
        var core = type.Methods.Single(m => m.Name == "Extract");
        if (!core.IsStatic || core.Parameters.Count != 4 || core.ReturnType.MetadataType != MetadataType.Boolean ||
            core.Parameters[1].ParameterType.MetadataType != MetadataType.String || core.Parameters[2].ParameterType.MetadataType != MetadataType.String)
            throw new InvalidDataException("OriginalAssets.Extract signature changed");
        string originalName = core.FullName;
        var callers = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Where(i => i.Operand is MethodReference r && r.FullName == originalName).ToArray();
        if (core.Body.ExceptionHandlers.Count != 1) throw new InvalidDataException("Extract exception handlers changed");
        var handler = core.Body.ExceptionHandlers[0];
        if (handler.HandlerType is not (ExceptionHandlerType.Filter or ExceptionHandlerType.Catch) || handler.HandlerStart.OpCode != OpCodes.Pop)
            throw new InvalidDataException("Extract IO exception handler changed");
        var il = core.Body.GetILProcessor(); var start = handler.HandlerStart;
        var entry = il.Create(OpCodes.Dup);
        il.InsertBefore(start, entry);
        il.InsertBefore(start, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(start, il.Create(OpCodes.Call, Import(module, nameof(PlatformGameBridge.RecordAssetIoError))));
        handler.HandlerStart = entry;
        core.Body.MaxStackSize = Math.Max(core.Body.MaxStackSize, 3);

        core.Name = "SwitchExtractCore";
        var wrapper = new MethodDefinition("Extract", core.Attributes, module.TypeSystem.Boolean);
        foreach (var parameter in core.Parameters) wrapper.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
        type.Methods.Add(wrapper);
        wrapper.Body.InitLocals = true;
        wrapper.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Boolean));
        il = wrapper.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, Import(module, nameof(PlatformGameBridge.ResolveAssetPath)));
        il.Emit(OpCodes.Starg, wrapper.Parameters[2]);
        foreach (var parameter in wrapper.Parameters) il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, core); il.Emit(OpCodes.Stloc_0);
        var done = il.Create(OpCodes.Ldloc_0);
        var unavailable = il.Create(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Brfalse, unavailable);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Call, Import(module, nameof(PlatformGameBridge.RecordAssetAvailable)));
        il.Emit(OpCodes.Br, done);
        il.Append(unavailable);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, Import(module, nameof(PlatformGameBridge.RecordAssetUnavailable)));
        il.Append(done); il.Emit(OpCodes.Ret);
        foreach (var caller in callers) caller.Operand = wrapper;
    }

    private static void WriteAndReplace(AssemblyDefinition assembly, string path)
    {
        // Horizon's rename rejects an existing destination even when .NET
        // requests overwrite. Serialize completely before opening the owned
        // patch-cache file with create/truncate; never replace a release input.
        // The cache completion marker/hashes are written only after all patches
        // and validation finish, so an interrupted write cannot be reused.
        foreach (var reference in assembly.MainModule.AssemblyReferences.Where(r => r.Name.StartsWith("System.") && r.Version.Major == 10))
            reference.Version = new Version(9, 0, 0, 0);
        using var image = new MemoryStream();
        assembly.Write(image);
        image.Position = 0;
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        image.CopyTo(output);
    }
}
