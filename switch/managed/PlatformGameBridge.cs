using WogShim;

namespace WogSwitch;

// Public because the separately loaded, patched game calls these methods.
public static class PlatformGameBridge
{
    private static LauncherSettings? settings;
    private static readonly object reportGate = new();
    private static readonly HashSet<string> assetIoErrors = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> unavailableAssets = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string>? preparedAssetFiles;
    private static readonly Dictionary<string, Dictionary<string, string[]>> characterFolders = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> reportedAliases = new(StringComparer.OrdinalIgnoreCase);
    private static bool assetSnapshotUnavailable;
    internal static int AssetSnapshotCount { get; private set; }
    internal static int AssetSnapshotChecks { get; private set; }
    internal static Exception? ReportedCrash { get; private set; }
    internal static Action<string>? Notice;
    internal static void Configure(LauncherSettings value) { settings = value; ReportedCrash = null; }

    internal static void BeginAssetReport()
    {
        lock (reportGate)
        {
            assetIoErrors.Clear();
            unavailableAssets.Clear();
            preparedAssetFiles = null; assetSnapshotUnavailable = false;
            characterFolders.Clear(); reportedAliases.Clear();
            AssetSnapshotCount = AssetSnapshotChecks = 0;
            File.WriteAllText(Path.Combine(settings!.Root, "asset-report.txt"),
                "Original asset diagnostics for this game session\nNOT_FOUND_OR_UNMAPPED = extraction returned false without an IO exception\nIO_ERROR = read or write raised an IO/access exception\n");
        }
    }

    public static void ReportUnavailable() => (Notice ?? Host.Log)("스위치에서는 게임 내 버그 리포트 창을 지원하지 않습니다. probe.log와 game-start.txt로 오류를 확인할 수 있습니다.");
    public static void Exit(int code) => throw new GameExitException(code);
    public static void Crash(Exception error)
    {
        // This entry may be called from the game or a worker exception hook.
        // Throwing a second custom exception here can abort Mono itself.
        ReportedCrash = error;
        try
        {
            string details = "GAME REPORTED ERROR:\n" + Diagnostics.Describe(error);
            if (settings != null) File.AppendAllText(Path.Combine(settings.Root, "game-start.txt"), details + "\n");
            Host.Log(details);
        }
        catch { /* Keep the original error from turning into a reporting failure. */ }
        WogShim.Win32.User32.PostQuitMessage(1);
    }

    public static void RecordAssetIoError(object error, string asset)
    {
        try
        {
            lock (reportGate)
            {
                if (settings == null || !assetIoErrors.Add(asset)) return;
                string details = error is Exception ex ? Diagnostics.Describe(ex) : error.GetType().Name;
                if (assetIoErrors.Count <= 3)
                {
                    string dest = ResolveAssetPath(Path.Combine(settings.SaveDir, "assets", asset));
                    string? parent = Path.GetDirectoryName(dest);
                    details += "\nPATH CHECK destination=" + dest + "; parentExists=" + Directory.Exists(parent);
                    if (parent != null) details += "; parentUTF8=" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(parent));
                }
                File.AppendAllText(Path.Combine(settings.Root, "asset-report.txt"), "IO_ERROR " + asset + "\n" + details + "\n");
            }
        }
        catch { /* Diagnostics must preserve the original extraction behavior. */ }
    }

    // Metadata shipped by the PC package and originals.txt may spell the same
    // character directory differently after an SD transfer. Resolve only the
    // first directory below characters, by NFC or a unique four-digit ID.
    // Keep the existing name on disk; never rename/delete edited metadata.
    public static string ResolveAssetPath(string path)
    {
        string full = Path.GetFullPath(LauncherSettings.NormalizeSdPath(path));
        var value = settings;
        if (value == null) return full;
        foreach (string assets in new[] { Path.Combine(value.SaveDir, "assets"), value.GameAssets })
        {
            string parent = Path.Combine(assets, "characters");
            if (!LauncherSettings.Contains(parent, full) || full.Equals(parent, StringComparison.OrdinalIgnoreCase)) continue;
            string relative = Path.GetRelativePath(parent, full);
            int split = relative.IndexOf(Path.DirectorySeparatorChar);
            string requested = split < 0 ? relative : relative[..split];
            if (requested.Length < 5 || requested[4] != '_' || !requested.Take(4).All(c => c >= '0' && c <= '9')) return full;
            lock (reportGate)
            {
                if (!characterFolders.TryGetValue(parent, out var map))
                {
                    if (!Directory.Exists(parent)) return full;
                    map = Directory.EnumerateDirectories(parent).Select(Path.GetFileName)
                        .Where(n => n != null && n.Length >= 5 && n[4] == '_' && n.Take(4).All(c => c >= '0' && c <= '9'))
                        .GroupBy(n => n![..4], StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Select(n => n!).ToArray(), StringComparer.OrdinalIgnoreCase);
                    characterFolders.Add(parent, map);
                }
                if (!map.TryGetValue(requested[..4], out var candidates)) return full;
                string? actual = candidates.FirstOrDefault(n => n.Equals(requested, StringComparison.OrdinalIgnoreCase));
                if (actual == null)
                {
                    var canonical = candidates.Where(n => n.Normalize().Equals(requested.Normalize(), StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (canonical.Length == 1) actual = canonical[0];
                    else if (candidates.Length == 1) actual = candidates[0];
                    // Multiple differently named folders with the same ID must
                    // not silently load some other user's character metadata.
                    else throw new IOException("Ambiguous character folders for " + requested + " under " + parent);
                }
                if (actual == requested) return full;
                if (reportedAliases.Add(Path.Combine(parent, requested)))
                    Host.Log("ASSET PATH alias: " + requested + " -> " + actual + " (existing character ID; no folder renamed)");
                return split < 0 ? Path.Combine(parent, actual) : Path.Combine(parent, actual, relative[(split + 1)..]);
            }
        }
        return full;
    }

    public static bool PreparedAssetExists(string path)
    {
        var value = settings;
        string full = ResolveAssetPath(path);
        string root = value == null ? "" : Path.Combine(value.SaveDir, "assets");
        if (value == null || !LauncherSettings.Contains(root, full)) return File.Exists(full);
        lock (reportGate)
        {
            if (!assetSnapshotUnavailable && preparedAssetFiles == null)
            {
                try
                {
                    preparedAssetFiles = new HashSet<string>(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);
                    AssetSnapshotCount++;
                    Host.Log("ASSET CACHE snapshot: " + preparedAssetFiles.Count + " files; repeated per-file existence checks use memory");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { assetSnapshotUnavailable = true; Host.Log("ASSET CACHE snapshot unavailable; using ordinary file checks"); }
            }
            if (preparedAssetFiles != null) { AssetSnapshotChecks++; return preparedAssetFiles.Contains(full); }
        }
        return File.Exists(full);
    }

    public static void BeginAssetPreparation()
    {
        lock (reportGate)
        {
            preparedAssetFiles = null; assetSnapshotUnavailable = false;
            characterFolders.Clear(); reportedAliases.Clear();
            AssetSnapshotCount = AssetSnapshotChecks = 0;
        }
    }

    public static void RecordAssetAvailable(string path)
    {
        lock (reportGate) preparedAssetFiles?.Add(Path.GetFullPath(LauncherSettings.NormalizeSdPath(path)));
    }

    public static void DeletePreparedAsset(string path)
    {
        path = ResolveAssetPath(path);
        CheckWritable(path);
        File.Delete(path);
        lock (reportGate) preparedAssetFiles?.Remove(Path.GetFullPath(LauncherSettings.NormalizeSdPath(path)));
    }

    public static void RecordAssetUnavailable(string asset, string destination)
    {
        try
        {
            lock (reportGate)
            {
                if (settings == null || !unavailableAssets.Add(asset)) return;
                string kind = assetIoErrors.Contains(asset) ? "UNAVAILABLE_AFTER_IO_ERROR" : "NOT_FOUND_OR_UNMAPPED";
                File.AppendAllText(Path.Combine(settings.Root, "asset-report.txt"), kind + " " + asset + " -> " + destination + "\n");
            }
        }
        catch { }
    }

    public static string GetFolderPath(Environment.SpecialFolder folder) => folder switch
    {
        Environment.SpecialFolder.ApplicationData or Environment.SpecialFolder.LocalApplicationData or
        Environment.SpecialFolder.MyDocuments or Environment.SpecialFolder.CommonApplicationData => settings?.SaveDir ?? throw new InvalidOperationException("Game paths not configured"),
        Environment.SpecialFolder.UserProfile => settings?.HomeDir ?? throw new InvalidOperationException("Game paths not configured"),
        Environment.SpecialFolder.Fonts => Path.Combine(settings?.Root ?? throw new InvalidOperationException("Game paths not configured"), "assets"),
        // A missing desktop/Windows special folder is represented by an empty
        // path. Never invoke the SDK's unsupported passwd/home lookup fallback.
        _ => string.Empty,
    };

    public static string? ScanPart2() => settings?.Part2Present == true ? settings.OriginalPart2 : null;
    public static string? ScanPart1() => settings?.Part1Present == true ? settings.OriginalPart1 : null;

    private static void CheckWritable(string path)
    {
        var value = settings ?? throw new InvalidOperationException("Game paths not configured");
        if (!LauncherSettings.Contains(value.Root, path) || new[] { value.ReleaseDir, value.OriginalPart1, value.OriginalPart2 }.Any(input => LauncherSettings.Contains(input, path)))
            throw new IOException("Game file writes must stay in the app's writable folders: " + path);
    }

    public static void CopyFile(string source, string destination) => CopyFile(source, destination, false);
    public static void CopyFile(string source, string destination, bool overwrite)
    {
        CheckWritable(destination);
        PortableFiles.Copy(source, destination, overwrite);
    }
    public static void MoveFile(string source, string destination) => MoveFile(source, destination, false);
    public static void MoveFile(string source, string destination, bool overwrite)
    {
        CheckWritable(source); CheckWritable(destination);
        PortableFiles.Move(source, destination, overwrite);
    }
}

public sealed class GameExitException(int code) : Exception("Game requested exit " + code)
{
    public int ExitCode { get; } = code;
}

public sealed class GameStartupException(Exception error) : Exception("Game startup failed: " + Diagnostics.Message(error), error);
