using System.Reflection;
using System.Runtime.ExceptionServices;
using WogShim;

namespace WogSwitch;

internal static class GameSession
{
    internal static int Start(LauncherSettings settings, Action<string> log, Func<int>? presentedFrames = null, LoadedGame? preloaded = null)
    {
        StartupMetrics.Reset(log);
        string report = Path.Combine(settings.Root, "game-start.txt");
        // Persist before entering the game and before calling native logging.
        // A native abort cannot execute our managed finally block.
        File.WriteAllLines(report, new[] { "WogSwitch 0.5.16 game startup", "Main invoked: no", "Frames observed at start: 0" });
        void Record(string line) { File.AppendAllText(report, line + "\n"); log(line); }
        Record("STEP game session entered; journal persisted before Main");
        object? oldBase = AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY");
        var variables = new[] { "DUELDX_SAVEDIR", "WAROFGENESIS_ORIGINAL", "WAROFGENESIS_PART1", "WAROFGENESIS_ASSETS" };
        var previous = variables.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            Record("STEP prepare game for startup");
            LoadedGame loaded;
            if (preloaded != null)
            {
                GameLoader.VerifySources(settings, preloaded.Prepared.SourceHashes);
                loaded = preloaded;
                Record("REUSE already loaded and verified game DLLs; source hashes rechecked");
            }
            else loaded = GameLoader.Load(GameLoader.Prepare(settings, Record), Record);
            if (loaded.Missing != 0) throw new NotSupportedException("Game start blocked by unresolved patch items: " + loaded.Missing);
            PlatformGameBridge.Configure(settings);
            string runtimeAssets = StartupAssets.Select(settings, Record);
            bool complete = File.Exists(Path.Combine(runtimeAssets, "data/TXR/Txr.dat")) && Directory.Exists(Path.Combine(runtimeAssets, "characters"));
            bool shipped = Directory.Exists(Path.Combine(settings.ReleaseDir, "assets/characters")) && File.Exists(Path.Combine(settings.ReleaseDir, "assets/originals.txt"));
            if (!complete && !shipped)
                throw new DirectoryNotFoundException("Copy the PC release assets folder (including characters and originals.txt) into release/assets before starting.");
            Directory.CreateDirectory(settings.SaveDir);
            PlatformGameBridge.BeginAssetReport();
            FrameProfiler.Reset(settings.Root, log);
            Environment.SetEnvironmentVariable("DUELDX_SAVEDIR", settings.SaveDir);
            Environment.SetEnvironmentVariable("WAROFGENESIS_ORIGINAL", settings.Part2Present ? settings.OriginalPart2 : null);
            Environment.SetEnvironmentVariable("WAROFGENESIS_PART1", settings.Part1Present ? settings.OriginalPart1 : null);
            // An incomplete shipped assets directory must go through OriginalAssets.Prepare.
            Environment.SetEnvironmentVariable("WAROFGENESIS_ASSETS", complete ? runtimeAssets : null);
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", settings.ReleaseDir + Path.DirectorySeparatorChar);
            StartupMetrics.Mark("X to Main invocation");
            Record("Main invoked: yes — " + loaded.Main.DeclaringType!.FullName + "." + loaded.Main.Name);
            try
            {
                loaded.Main.Invoke(null, loaded.Main.GetParameters().Length == 0 ? null : new object[] { Array.Empty<string>() });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is GameExitException exit)
            {
                Record("Game requested controlled exit: " + exit.ExitCode);
                return exit.ExitCode;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
            if (PlatformGameBridge.ReportedCrash is { } reported)
            {
                Record("FAIL game reported an error:\n" + Diagnostics.Describe(reported));
                return 1;
            }
            Record("Game Main returned normally; gameplay is not implied");
            return 0;
        }
        catch (Exception ex)
        {
            Record("FAIL game startup:\n" + Diagnostics.Describe(ex));
            return 1;
        }
        finally
        {
            FrameProfiler.Flush();
            Record("Game frames presented: " + (presentedFrames?.Invoke() ?? 0));
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", oldBase);
            foreach (var variable in previous) Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            Record("Startup cleanup completed");
        }
    }
}
