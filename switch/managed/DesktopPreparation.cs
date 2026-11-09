using System.Reflection;
using System.Runtime.ExceptionServices;
using WogShim;

namespace WogSwitch;

internal static class DesktopPreparation
{
    internal static int Run(string root, Action<string> log)
    {
        var settings = LauncherSettings.Read(root);
        string installedHost = Path.Combine(settings.Root, "managed/WogSwitch.Managed.dll");
        if (!File.Exists(installedHost) || GameLoader.Digest(installedHost) != GameLoader.Digest(typeof(PlatformGameBridge).Assembly.Location))
            throw new InvalidDataException("Copy this ZIP's complete switch/WogSwitchProbe folder to the SD card first. PC tools and installed managed host must match.");
        Host.LogSink = log;
        log("PC PREPARE: " + settings.Root + "; no game Main, GPU or audio is started");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var prepared = GameLoader.Prepare(settings, log);
        if (prepared.Missing.Length != 0) throw new NotSupportedException("Unresolved patch items: " + prepared.Missing.Length);
        var loaded = GameLoader.Load(prepared, log);
        int unavailable = -1;
        if (settings.Part2Present && Directory.Exists(Path.Combine(settings.ReleaseDir, "assets/characters")) &&
            File.Exists(Path.Combine(settings.ReleaseDir, "assets/originals.txt")))
        {
            object? oldBase = AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY");
            string? previous = Environment.GetEnvironmentVariable("DUELDX_SAVEDIR");
            try
            {
                PlatformGameBridge.Configure(settings);
                PlatformGameBridge.BeginAssetReport();
                Directory.CreateDirectory(settings.SaveDir);
                Environment.SetEnvironmentVariable("DUELDX_SAVEDIR", settings.SaveDir);
                AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", settings.ReleaseDir + Path.DirectorySeparatorChar);
                var assets = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "WarOfGenesis.Assets");
                var prepare = assets.GetType("WarOfGenesis.Assets.OriginalAssets", true)!.GetMethod("Prepare", BindingFlags.Public | BindingFlags.Static)!;
                Action<int, int> progress = (done, total) => { if (done % 100 == 0 || done == total) log("PC ASSETS " + done + "/" + total); };
                try { unavailable = (int)prepare.Invoke(null, new object?[] { settings.OriginalPart2, progress })!; }
                catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
            }
            finally
            {
                AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", oldBase);
                Environment.SetEnvironmentVariable("DUELDX_SAVEDIR", previous);
            }
        }
        else log("PC ASSETS skipped: part2 originals or shipped release/assets are not present; DLL cache is still prepared");
        string summary = "PC PREPARE COMPLETE\nCache: " + (prepared.CacheHit ? "verified hit" : "built") +
            "\nGame version: " + loaded.Version + "\nMain invoked: no\nUnavailable original assets: " + unavailable +
            "\nElapsed seconds on this PC: " + watch.Elapsed.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "\n";
        File.WriteAllText(Path.Combine(settings.Root, "pc-prepare-report.txt"), summary);
        log(summary);
        return 0;
    }
}
