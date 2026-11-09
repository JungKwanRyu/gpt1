using System.Diagnostics;
using System.Text.Json;
using WogPatcher;
using WogSwitch;

internal static class PerformanceSmoke
{
    internal static void Verify(string original, string patched, LauncherSettings settings, string output)
    {
        string baseline = Path.Combine(output, "validator-baseline");
        var originalReport = GamePatcher.Patch(original, settings.ShimPath, baseline);
        var negative = PatchValidation.Verify(baseline, settings.ShimPath, Console.WriteLine);
        if (negative.Missing.Count == 0 || !negative.Missing.ToHashSet().SetEquals(originalReport.Missing))
            throw new InvalidDataException("Read-only verifier must detect exactly the original validator's negative baseline");
        var before = GamePatcher.GameAssemblies.ToDictionary(n => n, n => GameLoader.Digest(Path.Combine(patched, n)));
        var watch = Stopwatch.StartNew();
        var fast = PatchValidation.Verify(patched, settings.ShimPath, Console.WriteLine);
        double fastMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var full = GamePatcher.Patch(patched, settings.ShimPath, Path.Combine(output, "full-validation-comparison"));
        double fullMs = watch.Elapsed.TotalMilliseconds;
        if (!fast.Missing.ToHashSet().SetEquals(full.Missing) || fast.Missing.Count != 0 ||
            !GamePatcher.GameAssemblies.All(n => before[n] == GameLoader.Digest(Path.Combine(patched, n))))
            throw new InvalidDataException("Read-only verifier changed a DLL or disagreed with the full pass");
        File.WriteAllText(Path.Combine(output, "verification-timing.json"), JsonSerializer.Serialize(new {
            environment = "Linux .NET; not a Switch speed measurement", read_only_ms = fastMs,
            full_repatch_ms = fullMs, negative_missing = negative.Missing.Count, patched_missing = fast.Missing.Count,
            dll_bytes_unchanged = true }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS read-only verifier matches the full original pass for compatible and incompatible DLLs and preserves all DLL bytes");
    }

    internal static void Logging()
    {
        var limiter = new ProgressLogLimiter();
        int kept = 0;
        for (int i = 0; i <= 3238; i++)
            if (limiter.Keep("[wog] [form] 원본 게임 폴더에서 자료를 꺼내는 중… " + i + "/3238", i)) kept++;
        if (kept > 9 || !limiter.Keep("AUDIO ERROR must always persist", 3239) ||
            !limiter.Keep("[wog] MessageBox missing files warning", 3239)) throw new InvalidDataException("Progress throttling dropped a critical message or kept every update");
        Console.WriteLine("PASS progress log coalescing keeps first/final/error messages; " + kept + " writes for 3239 synthetic rapid updates");
    }

    internal static void Snapshot(LauncherSettings settings)
    {
        string folder = Path.Combine(settings.SaveDir, "assets"); Directory.CreateDirectory(folder);
        string old = Path.Combine(folder, "snapshot-old.txt"), created = Path.Combine(folder, "snapshot-new.txt");
        File.WriteAllText(old, "old");
        PlatformGameBridge.BeginAssetReport();
        if (!PlatformGameBridge.PreparedAssetExists(old)) throw new InvalidDataException("Existing asset missing from snapshot");
        for (int i = 0; i < 3238; i++) if (!PlatformGameBridge.PreparedAssetExists(old)) throw new InvalidDataException("Cached asset disappeared");
        File.WriteAllText(created, "new"); PlatformGameBridge.RecordAssetAvailable(created);
        if (!PlatformGameBridge.PreparedAssetExists(created)) throw new InvalidDataException("Extraction did not update the snapshot");
        PlatformGameBridge.DeletePreparedAsset(old);
        if (PlatformGameBridge.PreparedAssetExists(old) || File.Exists(old)) throw new InvalidDataException("Deleted patched asset stayed cached");
        if (PlatformGameBridge.AssetSnapshotCount != 1 || PlatformGameBridge.AssetSnapshotChecks < 3238)
            throw new InvalidDataException("Repeated existence checks rebuilt the directory snapshot");
        string external = Path.Combine(folder, "snapshot-external.txt"); File.WriteAllText(external, "external");
        PlatformGameBridge.BeginAssetPreparation();
        if (!PlatformGameBridge.PreparedAssetExists(external)) throw new InvalidDataException("A new preparation pass did not refresh the snapshot");
        Console.WriteLine("PASS one directory snapshot serves 3238 repeated asset checks and tracks newly extracted/deleted patched assets");
    }
}
