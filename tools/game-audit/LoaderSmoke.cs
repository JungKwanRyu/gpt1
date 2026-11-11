using Mono.Cecil;
using WogPatcher;
using WogSwitch;

internal static class LoaderSmoke
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException("Loader smoke failed: " + message);
        Console.WriteLine("PASS " + message);
    }

    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { Console.WriteLine("PASS " + message); return; }
        throw new InvalidDataException("Expected " + typeof(T).Name + ": " + message);
    }

    internal static void Run(string fixture, string output)
    {
        string root = Path.Combine(output, "app"), release = Path.Combine(root, "release");
        Directory.CreateDirectory(release);
        Directory.CreateDirectory(Path.Combine(root, "managed"));
        PortableFiles.Copy(typeof(GameLayout).Assembly.Location, Path.Combine(root, "managed/WogPatcher.dll"), true);
        PortableFiles.Copy(typeof(WogShim.Host).Assembly.Location, Path.Combine(root, "managed/WogShim.dll"), true);
        var settings = LauncherSettings.Read(root);
        Require(LauncherSettings.NormalizeSdPath("sdmc:/switch/WogSwitchProbe") == "/switch/WogSwitchProbe", "SD mount notation normalizes to an absolute managed path");
        Reject<FileNotFoundException>(() => GameLoader.Prepare(settings, Console.WriteLine), "missing game DLLs are reported before patching");
        string ini = Path.Combine(root, "launcher.ini");
        File.WriteAllText(ini, "release=..\n");
        Reject<InvalidDataException>(() => LauncherSettings.Read(root), "patch output cannot overlap configured input folders");
        File.WriteAllText(ini, "release=" + Path.GetPathRoot(root) + "\n");
        Reject<InvalidDataException>(() => LauncherSettings.Read(root), "the filesystem root cannot contain both game inputs and patch output");
        File.WriteAllText(ini, "relese=release\n");
        Reject<InvalidDataException>(() => LauncherSettings.Read(root), "unknown configuration keys are rejected");
        File.WriteAllText(ini, "release=release\nrelease=elsewhere\n");
        Reject<InvalidDataException>(() => LauncherSettings.Read(root), "duplicate configuration keys are rejected");
        File.WriteAllText(ini, "[game]\nrelease=release\noriginal=original/part2\npart1=original/part1\nassets=release/assets\n");
        settings = LauncherSettings.Read(root);
        PlatformGameBridge.Configure(settings);
        foreach (var folder in Enum.GetValues<Environment.SpecialFolder>())
        {
            string mapped = PlatformGameBridge.GetFolderPath(folder);
            Require(mapped.Length == 0 || LauncherSettings.Contains(settings.Root, mapped), "special folder stays inside the app or is unavailable: " + folder);
        }
        Require(PlatformGameBridge.GetFolderPath(Environment.SpecialFolder.UserProfile) == settings.HomeDir, "user profile maps to the app home without an OS passwd lookup");
        Require(PlatformGameBridge.GetFolderPath(Environment.SpecialFolder.ApplicationData) == settings.SaveDir, "game application data maps to the configured save folder");
        foreach (string file in GamePatcher.GameAssemblies) PortableFiles.Copy(Path.Combine(fixture, file), Path.Combine(release, file), true);
        var originalHashes = GamePatcher.GameAssemblies.ToDictionary(n => n, n => GameLoader.Digest(Path.Combine(release, n)));
        var first = GameLoader.Prepare(settings, Console.WriteLine);
        Require(!first.CacheHit && first.Missing.Length == 0, "real PC game platform patches pass the original patcher's compatibility recheck");
        Require(File.ReadAllText(Path.Combine(first.Directory, "upstream-compatibility.txt")).Contains("missing 90"), "the original 90-item report is preserved separately");
        PerformanceSmoke.Verify(release, first.Directory, settings, output);
        RuntimeApiAudit.RunGames(output, first.Directory);
        bool reparsed=false;
        var second = GameLoader.Prepare(settings,line=>{if(line.StartsWith("STEP inspect original DLL"))reparsed=true;Console.WriteLine(line);});
        Require(!reparsed,"verified cache hit avoids repeated source DLL metadata parsing");
        Require(second.CacheHit && second.Directory == first.Directory && second.Missing.SequenceEqual(first.Missing), "cache reuse verifies DLL hashes and keeps the compatibility warnings");
        File.AppendAllText(Path.Combine(first.Directory, "WarOfGenesis.dll"), "tampered-cache");
        var repaired = GameLoader.Prepare(settings, Console.WriteLine);
        Require(!repaired.CacheHit && Directory.GetDirectories(settings.CacheRoot, "*.incomplete-*").Length == 1, "changed patch cache is preserved and rebuilt before loading");
        string game = Path.Combine(release, "WarOfGenesis.dll");
        byte[] changed = File.ReadAllBytes(game);
        int pe = BitConverter.ToInt32(changed, 0x3c);
        changed[pe + 8] ^= 1; // COFF timestamp: same assembly identity, different source bytes.
        File.WriteAllBytes(game, changed);
        var revised = GameLoader.Prepare(settings, Console.WriteLine);
        Require(revised.Directory != first.Directory, "changed source DLL content invalidates the cache even when version and MVID are unchanged");
        PortableFiles.Copy(Path.Combine(fixture, "WarOfGenesis.dll"), game, true);
        string assets = Path.Combine(release, "WarOfGenesis.Assets.dll");
        using (var assembly = AssemblyDefinition.ReadAssembly(assets))
        {
            assembly.Name.Version = new Version(0, 0, 0, 1);
            assembly.Write(assets + ".different");
        }
        PortableFiles.Move(assets + ".different", assets, true);
        Reject<InvalidDataException>(() => GameLoader.Prepare(settings, Console.WriteLine), "mixed PC game DLL versions are rejected");
        PortableFiles.Copy(Path.Combine(fixture, "WarOfGenesis.Assets.dll"), assets, true);
        File.WriteAllText(settings.ReportPath, "STALE_PREVIOUS_SUCCESS");
        bool freshReportObserved = false;
        string[] summary = GameLoader.Run(settings, line => {
            string current = File.ReadAllText(settings.ReportPath);
            if (line.StartsWith("STEP SHA256 original DLL"))
                freshReportObserved = !current.Contains("STALE_PREVIOUS_SUCCESS") && current.Contains(line) && current.Contains("Load check status: in progress");
            Console.WriteLine(line);
        });
        Require(freshReportObserved, "current-run load journal replaces stale success and persists steps before logging callbacks");
        Require(summary[0].Contains("완료"), "real patched game DLLs load and the Main signature is verified");
        Require(GamePatcher.GameAssemblies.All(n => GameLoader.Digest(Path.Combine(release, n)) == originalHashes[n]), "original PC DLLs are unchanged");
        Require(!Directory.Exists(settings.SaveDir), "load validation creates no game save folder");
        string report = File.ReadAllText(settings.ReportPath);
        Require(report.Contains("Main invoked: no") && report.Contains("unresolved items="), "load report distinguishes DLL loading from game execution");
        PortableFiles.Copy(settings.ReportPath, Path.Combine(output, "load-report.txt"), true);
        PortableFiles.Copy(Path.Combine(first.Directory, "compatibility.txt"), Path.Combine(output, "patch-report.txt"), true);
        File.WriteAllLines(Path.Combine(output, "loader-smoke-summary.txt"), summary);
        Console.WriteLine("PASS REAL GAME LOADER SMOKE COMPLETE; Main was not invoked");
        StartupSmoke.Run(fixture, root, output, settings, first.Directory);
    }
}
