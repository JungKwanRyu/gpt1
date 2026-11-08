using WogShim;

namespace WogSwitch;

// Public because the separately loaded, patched game calls these methods.
public static class PlatformGameBridge
{
    private static LauncherSettings? settings;
    internal static Action<string>? Notice;
    internal static void Configure(LauncherSettings value) => settings = value;

    public static void ReportUnavailable() => (Notice ?? Host.Log)("스위치에서는 게임 내 버그 리포트 창을 지원하지 않습니다. probe.log와 game-start.txt로 오류를 확인할 수 있습니다.");
    public static void Exit(int code) => throw new GameExitException(code);
    public static void Crash(Exception error) => throw new GameStartupException(error);

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
