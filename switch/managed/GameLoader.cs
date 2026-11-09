using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using WogPatcher;

namespace WogSwitch;

internal sealed record PreparedGame(string Directory, string Version, bool CacheHit,
    string[] Missing, Dictionary<string, string> SourceHashes);
internal sealed record LoadedGame(string Version, int Assemblies, string EntryPoint, int Missing, MethodInfo Main, PreparedGame Prepared);

internal static class GameLoader
{
    private const string CacheVersion = "wog-switch-loader-0.5-v1";

    internal static string Digest(string filename)
    {
        using var stream = File.OpenRead(filename);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    internal static PreparedGame Prepare(LauncherSettings settings, Action<string> log)
    {
        var hashes = new Dictionary<string, string>();
        Version? version = null;
        foreach (string name in GamePatcher.GameAssemblies)
        {
            Diagnostics.Step("inspect original DLL " + name, log);
            string file = Path.Combine(settings.ReleaseDir, name);
            if (!File.Exists(file)) throw new FileNotFoundException("Copy the PC release DLL into release/: " + name, file);
            using var assembly = AssemblyDefinition.ReadAssembly(file);
            if (assembly.Name.Name != Path.GetFileNameWithoutExtension(name))
                throw new InvalidDataException("DLL filename and assembly identity differ: " + name);
            if (!assembly.MainModule.Attributes.HasFlag(ModuleAttributes.ILOnly) ||
                assembly.MainModule.Attributes.HasFlag(ModuleAttributes.Required32Bit))
                throw new InvalidDataException("Expected a portable managed game DLL: " + name);
            if (assembly.MainModule.AssemblyReferences.Any(r => r.Name == "WogShim"))
                throw new InvalidDataException("Use original PC DLLs in release/, not already patched DLLs: " + name);
            if (version != null && version != assembly.Name.Version)
                throw new InvalidDataException("The two game DLL versions differ; copy both from the same PC release.");
            version = assembly.Name.Version;
            Diagnostics.Step("SHA256 original DLL " + name, log);
            hashes[name] = Digest(file);
        }
        if (!File.Exists(settings.ShimPath)) throw new FileNotFoundException("Bundled WogShim missing", settings.ShimPath);
        string patcherPath = Path.Combine(settings.Root, "managed", "WogPatcher.dll");
        if (!File.Exists(patcherPath)) throw new FileNotFoundException("Bundled WogPatcher missing", patcherPath);
        Diagnostics.Step("compute patch cache identity", log);
        string material = CacheVersion + "\n" + PlatformPatches.Version + "\n" + typeof(PlatformGameBridge).Assembly.ManifestModule.ModuleVersionId +
            "\n" + Digest(settings.ShimPath) + "\n" + Digest(patcherPath) + "\n" +
            string.Join("\n", GamePatcher.GameAssemblies.Select(n => n + ":" + hashes[n]));
        string key;
        using (var sha = SHA256.Create()) key = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        string directory = Path.Combine(settings.CacheRoot, key);
        bool cached = CacheValid(directory);
        if (!cached)
        {
            if (System.IO.Directory.Exists(directory))
            {
                string preserved = directory + ".incomplete-" + DateTime.UtcNow.Ticks;
                System.IO.Directory.Move(directory, preserved);
                log("Incomplete or changed patch cache preserved at " + preserved);
            }
            var layout = new GameLayout(settings.ReleaseDir, directory, settings.SaveDir, null, settings.ShimPath);
            Diagnostics.Step("original GameLauncher.Prepare", log);
            string prepared = GameLauncher.Prepare(layout, log);
            if (!Path.GetFullPath(prepared).Equals(directory, StringComparison.Ordinal))
                throw new InvalidDataException("Patcher returned an unexpected output folder");
            var report = GameLauncher.LastReport ?? throw new InvalidDataException("Patcher did not return a compatibility report");
            File.WriteAllText(Path.Combine(directory, "upstream-compatibility.txt"), report.ToString());
            var changes = new List<string>();
            Diagnostics.Step("apply Switch platform patches", log);
            PlatformPatches.Apply(directory, line => { changes.Add(line); log(line); });
            File.WriteAllLines(Path.Combine(directory, "platform-patches.txt"), changes);
            // Run the original validator without repeating Retarget, RedirectCalls,
            // RewritePInvokes, serialization and a second pair of DLL copies.
            Diagnostics.Step("verify Switch platform patches", log);
            var finalReport = PatchValidation.Verify(directory, settings.ShimPath, log);
            File.WriteAllText(Path.Combine(directory, "compatibility.txt"), finalReport.ToString());
            File.WriteAllLines(Path.Combine(directory, "missing.txt"), finalReport.Missing);
            log("Post-platform-patch compatibility: " + finalReport.Missing.Count + " unresolved items");
            foreach (string name in GamePatcher.GameAssemblies)
            {
                using var patched = AssemblyDefinition.ReadAssembly(Path.Combine(directory, name));
                if (patched.Name.Version != version) throw new InvalidDataException("Patched DLL version mismatch: " + name);
            }
            VerifySources(settings, hashes);
            File.WriteAllLines(Path.Combine(directory, "complete.sha256"),
                CacheFiles().Select(name => Digest(Path.Combine(directory, name)) + "  " + name));
        }
        VerifySources(settings, hashes);
        string[] missing = File.ReadAllLines(Path.Combine(directory, "missing.txt"));
        log("PASS original game DLL patch preparation; version=" + version + "; cache=" + (cached ? "verified hit" : "built"));
        log("Compatibility report: " + missing.Length + " unresolved items; game execution remains unverified");
        return new PreparedGame(directory, version!.ToString(), cached, missing, hashes);
    }

    private static IEnumerable<string> CacheFiles() => GamePatcher.GameAssemblies.Concat(new[] { "compatibility.txt", "missing.txt", "upstream-compatibility.txt", "platform-patches.txt" });

    private static bool CacheValid(string directory)
    {
        string marker = Path.Combine(directory, "complete.sha256");
        if (!File.Exists(marker)) return false;
        try
        {
            string[] lines = File.ReadAllLines(marker);
            string[] names = CacheFiles().ToArray();
            if (lines.Length != names.Length) return false;
            for (int i = 0; i < names.Length; i++)
                if (!File.Exists(Path.Combine(directory, names[i])) ||
                    lines[i] != Digest(Path.Combine(directory, names[i])) + "  " + names[i]) return false;
            return true;
        }
        catch (IOException) { return false; }
    }

    internal static void VerifySources(LauncherSettings settings, Dictionary<string, string> hashes)
    {
        foreach (var item in hashes)
            if (Digest(Path.Combine(settings.ReleaseDir, item.Key)) != item.Value)
                throw new InvalidDataException("Source DLL changed while patching: " + item.Key);
    }

    internal static LoadedGame Load(PreparedGame prepared, Action<string> log)
    {
        var allowed = GamePatcher.GameAssemblies.ToDictionary(n => Path.GetFileNameWithoutExtension(n)!, n => n, StringComparer.Ordinal);
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name) =>
            name.Name != null && allowed.TryGetValue(name.Name, out string? file)
                ? context.LoadFromAssemblyPath(Path.Combine(prepared.Directory, file)) : null;
        // Only the two patched game DLLs may resolve here. Windows runtime DLLs
        // copied alongside the PC game must never replace the Switch BCL.
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            Assembly? game = null;
            int count = 0;
            foreach (string file in ((IEnumerable<string>)GamePatcher.GameAssemblies).Reverse())
            {
                string path = Path.Combine(prepared.Directory, file);
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                string location = Path.GetFullPath(LauncherSettings.NormalizeSdPath(assembly.Location));
                if (!location.Equals(path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidDataException("Game DLL was loaded from another folder; restart the app: " + assembly.Location);
                if (assembly.GetName().Version?.ToString() != prepared.Version)
                    throw new InvalidDataException("Loaded game DLL version differs from the prepared version");
                log("PASS patched assembly loaded: " + assembly.GetName().Name + " from " + location);
                count++;
                if (file == "WarOfGenesis.dll") game = assembly;
            }
            var main = game?.GetType("DuelDx.Program", true)?.GetMethod("Main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (main == null || !main.IsStatic || main.ReturnType != typeof(void) ||
                !(main.GetParameters().Length == 0 ||
                  (main.GetParameters().Length == 1 && main.GetParameters()[0].ParameterType == typeof(string[]))))
                throw new MissingMethodException("Unsupported DuelDx.Program.Main signature");
            log("PASS patched game entry point lookup: " + main);
            log("LOAD CHECK COMPLETE; Main was not invoked; unresolved items=" + prepared.Missing.Length);
            return new LoadedGame(prepared.Version, count, main.ToString()!, prepared.Missing.Length, main, prepared);
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }

    internal static string[] Run(LauncherSettings settings, Action<string> log, Action<LoadedGame>? loadedCallback = null)
    {
        var report = new List<string> { "WogSwitch 0.5.10 game DLL patch/load check", "Load check status: in progress", "Main invoked: no",
            "PC release: " + settings.ReleaseDir, "Part 2 folder detected: " + settings.Part2Present,
            "Part 1 folder detected: " + settings.Part1Present, "Game assets/characters detected: " +
                System.IO.Directory.Exists(Path.Combine(settings.GameAssets, "characters")) };
        File.WriteAllLines(settings.ReportPath, report);
        void Record(string line) { report.Add(line); File.AppendAllText(settings.ReportPath, line + "\n"); log(line); }
        try
        {
            foreach (string line in report.ToArray()) log(line);
            var prepared = Prepare(settings, Record);
            var loaded = Load(prepared, Record);
            loadedCallback?.Invoke(loaded);
            report[1] = "Load check status: complete";
            report.Add("Patch output: " + prepared.Directory);
            report.Add("Compatibility report follows:");
            report.AddRange(prepared.Missing);
            return new[] { "게임 DLL 패치·로딩 확인 완료", "PC version " + loaded.Version + " / DLLs " + loaded.Assemblies,
                "미지원 항목 " + loaded.Missing + "개 — X 버튼으로 시작 시도", "결과: load-report.txt / probe.log" };
        }
        catch (Exception ex)
        {
            report[1] = "Load check status: failed";
            Record("FAIL game load check:\n" + Diagnostics.Describe(ex));
            throw;
        }
        finally { File.WriteAllLines(settings.ReportPath, report); }
    }
}
