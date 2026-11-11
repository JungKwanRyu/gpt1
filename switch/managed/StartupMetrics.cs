using System.Diagnostics;
namespace WogSwitch;
public static class StartupMetrics
{
    private static Stopwatch? watch;
    private static Action<string>? logger;
    private static bool titleSeen;
    internal static void Reset(Action<string> log) { watch=Stopwatch.StartNew();logger=log;titleSeen=false; }
    internal static void Mark(string stage) => logger?.Invoke("STARTUP "+stage+" elapsed="+(watch?.Elapsed.TotalSeconds??0).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"s");
    public static void MarkTitleDrawn() { if(titleSeen || watch==null)return;titleSeen=true;Mark("X to title composition complete"); }
}
