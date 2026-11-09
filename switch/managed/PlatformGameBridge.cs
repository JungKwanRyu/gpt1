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
                File.AppendAllText(Path.Combine(settings.Root, "asset-report.txt"), "IO_ERROR " + asset + "\n" + details + "\n");
            }
        }
        catch { /* Diagnostics must preserve the original extraction behavior. */ }
    }

    public static bool PreparedAssetExists(string path)
    {
        var value = settings;
        string full = Path.GetFullPath(LauncherSettings.NormalizeSdPath(path));
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
            AssetSnapshotCount = AssetSnapshotChecks = 0;
        }
    }

    public static void RecordAssetAvailable(string path)
    {
        lock (reportGate) preparedAssetFiles?.Add(Path.GetFullPath(LauncherSettings.NormalizeSdPath(path)));
    }

    public static void DeletePreparedAsset(string path)
    {
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
