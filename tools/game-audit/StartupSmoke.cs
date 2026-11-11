using WogShim;
using WogSwitch;

internal static class StartupSmoke
{
    internal static void Run(string fixture, string root, string output, LauncherSettings settings, string patched)
    {
        var host = new CancelHost(); Host.Use(host); Host.LogSink = Console.WriteLine;
        int missingAssets = GameSession.Start(settings, Console.WriteLine);
        if (missingAssets != 1 || host.AskCount != 0 || File.ReadAllText(Path.Combine(root, "game-start.txt")).Contains("Main invoked: yes"))
            throw new InvalidDataException("Missing shipped assets must prevent Main invocation");
        Console.WriteLine("PASS missing PC assets block Main invocation with a concrete preparation error");
        string assets = Path.Combine(fixture, "assets");
        foreach (string file in Directory.GetFiles(assets, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(settings.ReleaseDir, "assets", Path.GetRelativePath(assets, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!); PortableFiles.Copy(file, to, true);
        }
        PlatformGameBridge.Notice = text => Console.WriteLine("NOTICE " + text);
        foreach (string file in WogPatcher.GamePatcher.GameAssemblies)
        {
            using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(patched, file));
            if (assembly.MainModule.GetTypes().SelectMany(t => t.Methods).Any(m => m.IsPInvokeImpl))
                throw new InvalidDataException("An unhandled game P/Invoke remains: " + file);
        }
        Console.WriteLine("PASS no direct native imports remain in the two patched game DLLs");
        bool journalObservedBeforeMain = false;
        bool reused = false, repeatedInspection = false;
        var preloaded = GameLoader.Load(GameLoader.Prepare(settings, Console.WriteLine), Console.WriteLine);
        int code = GameSession.Start(settings, line => {
            if (line.StartsWith("REUSE already loaded")) reused = true;
            if (line.StartsWith("STEP inspect original DLL")) repeatedInspection = true;
            if (line.StartsWith("Main invoked: yes"))
                journalObservedBeforeMain = File.ReadAllText(Path.Combine(root, "game-start.txt")).Contains(line);
            Console.WriteLine(line);
        }, preloaded: preloaded);
        if (!reused || repeatedInspection) throw new InvalidDataException("X startup repeated preparation instead of reusing the validated load");
        Console.WriteLine("PASS startup reuses already loaded DLLs while rechecking original hashes");
        if (!journalObservedBeforeMain) throw new InvalidDataException("Main marker was not written before invoking native logging/game code");
        Console.WriteLine("PASS game-start journal persisted before Main invocation and before logging callbacks");
        if (code != 0 || host.AskCount == 0 || host.GraphicsCalls != 0)
            throw new InvalidDataException("Actual Main should reach the original-folder prompt and return after cancel: code=" + code + ", asks=" + host.AskCount + ", graphics=" + host.GraphicsCalls);
        string report = File.ReadAllText(Path.Combine(root, "game-start.txt"));
        if (!report.Contains("Main invoked: yes") || !report.Contains("Main returned normally")) throw new InvalidDataException("Main execution was not recorded");
        PortableFiles.Copy(Path.Combine(root, "game-start.txt"), Path.Combine(output, "game-start.txt"), true);
        Console.WriteLine("PASS ACTUAL GAME MAIN: original-folder dialog reached; cancellation returned normally; no GPU/gameplay claimed");
        var game = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "WarOfGenesis");
        var reportType = game.GetType("DuelDx.MyBugReportsDialog", true)!;
        var shot = reportType.GetMethod("ShotIfAsked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        if (!Equals(shot.Invoke(null, null), false)) throw new InvalidDataException("Report screenshot hook still runs");
        Console.WriteLine("PASS rewritten startup report hook executes and returns false");
        int notices = 0; PlatformGameBridge.Notice = _ => notices++;
        reportType.GetMethod("Show", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new object?[] { null });
        var ask = game.GetType("DuelDx.BugReportDialog", true)!.GetMethod("Ask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        if (ask.Invoke(null, new object?[] { null, "test" }) != null || notices != 2) throw new InvalidDataException("Report dialogs did not execute their notice/cancel replacements");
        Console.WriteLine("PASS report UI entry points execute the host notice and cancel without WinForms controls");
        try { PlatformGameBridge.Exit(7); } catch (GameExitException ex) when (ex.ExitCode == 7) { Console.WriteLine("PASS controlled game exit preserves the host process"); }
        StartupAssetsSmoke.Run(output);
        RasterSmoke.Run(game);
        SharedRasterSmoke.Run(game, root);
        FieldRasterSmoke.Run(game);
        PlaybackEffectSmoke.Run(game);
        StreamingTalkSmoke.Run(game, output);
        MixerKernelSmoke.Run();
        RealMixerSmoke.Run();
        AssetDiagnosticsSmoke.Run(root, output, settings);
        PerformanceSmoke.Snapshot(settings);
        PlatformGameBridge.Crash(new InvalidOperationException("SYNTHETIC_REPORTED_FAILURE"));
        if (PlatformGameBridge.ReportedCrash?.Message != "SYNTHETIC_REPORTED_FAILURE" ||
            !File.ReadAllText(Path.Combine(root, "game-start.txt")).Contains("SYNTHETIC_REPORTED_FAILURE"))
            throw new InvalidDataException("Game crash reporter did not retain the error");
        Console.WriteLine("PASS game crash reporting records the original error and posts quit without throwing a second exception");
    }

    private sealed class CancelHost : IHost
    {
        internal int AskCount, GraphicsCalls;
        public (int W, int H) SurfaceSize => (1280, 720);
        public bool IsGles => true;
        public bool ListDrives => false;
        public bool HasTouch => false;
        public nint GetProcAddress(string name) { GraphicsCalls++; throw new InvalidOperationException("Unexpected graphics before source selection"); }
        public void SwapBuffers() => throw new InvalidOperationException("Unexpected rendering");
        public void PumpEvents() { }
        public void ShowMessage(string caption, string text) => Console.WriteLine("MESSAGE " + caption + ": " + text);
        public int Ask(string caption, string text, string[] buttons)
        {
            AskCount++; Console.WriteLine("ACTUAL ORIGINAL-FOLDER PROMPT: " + caption + " / " + string.Join(", ", buttons));
            return buttons.Length - 1;
        }
        public string PickFolder(string description) => null!;
        public IAudioSink OpenAudio(int sampleRate, int channels, int bitsPerSample) => throw new InvalidOperationException("Unexpected audio before source selection");
    }
}
