using System.Reflection;
using System.Runtime.ExceptionServices;
using WogShim;

namespace WogSwitch;

internal static class GameSession
{
    internal static int Start(LauncherSettings settings, Action<string> log)
    {
        var lines = new List<string> { "WogSwitch 0.5 game startup", "Main invoked: no", "First frame: not observed" };
        void Record(string line) { lines.Add(line); log(line); }
        object? oldBase = AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY");
        var variables = new[] { "DUELDX_SAVEDIR", "WAROFGENESIS_ORIGINAL", "WAROFGENESIS_PART1", "WAROFGENESIS_ASSETS" };
        var previous = variables.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            var prepared = GameLoader.Prepare(settings, Record);
            if (prepared.Missing.Length != 0) throw new NotSupportedException("Game start blocked by unresolved patch items: " + prepared.Missing.Length);
            var loaded = GameLoader.Load(prepared, Record);
            PlatformGameBridge.Configure(settings);
            Directory.CreateDirectory(settings.SaveDir);
            Environment.SetEnvironmentVariable("DUELDX_SAVEDIR", settings.SaveDir);
            Environment.SetEnvironmentVariable("WAROFGENESIS_ORIGINAL", settings.Part2Present ? settings.OriginalPart2 : null);
            Environment.SetEnvironmentVariable("WAROFGENESIS_PART1", settings.Part1Present ? settings.OriginalPart1 : null);
            // An incomplete shipped assets directory must go through OriginalAssets.Prepare.
            bool complete = File.Exists(Path.Combine(settings.GameAssets, "data/TXR/Txr.dat")) && Directory.Exists(Path.Combine(settings.GameAssets, "characters"));
            Environment.SetEnvironmentVariable("WAROFGENESIS_ASSETS", complete ? settings.GameAssets : null);
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", settings.ReleaseDir + Path.DirectorySeparatorChar);
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
            Record("Game Main returned normally; gameplay is not implied");
            return 0;
        }
        catch (Exception ex)
        {
            Record("FAIL game startup: " + ex);
            return 1;
        }
        finally
        {
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", oldBase);
            foreach (var variable in previous) Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            File.WriteAllLines(Path.Combine(settings.Root, "game-start.txt"), lines);
        }
    }
}
