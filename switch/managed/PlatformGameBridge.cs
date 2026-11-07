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
        Environment.SpecialFolder.MyDocuments => settings?.SaveDir ?? throw new InvalidOperationException("Game paths not configured"),
        Environment.SpecialFolder.UserProfile => settings?.Root ?? throw new InvalidOperationException("Game paths not configured"),
        _ => Environment.GetFolderPath(folder),
    };

    public static string? ScanPart2() => settings?.Part2Present == true ? settings.OriginalPart2 : null;
    public static string? ScanPart1() => settings?.Part1Present == true ? settings.OriginalPart1 : null;
}

public sealed class GameExitException(int code) : Exception("Game requested exit " + code)
{
    public int ExitCode { get; } = code;
}

public sealed class GameStartupException(Exception error) : Exception("Game startup failed: " + error.Message, error);
