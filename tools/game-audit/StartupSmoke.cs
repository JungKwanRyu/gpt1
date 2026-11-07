using WogShim;
using WogSwitch;

internal static class StartupSmoke
{
    internal static void Run(string fixture, string root, string output, LauncherSettings settings, string patched)
    {
        string assets = Path.Combine(fixture, "assets");
        foreach (string file in Directory.GetFiles(assets, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(settings.ReleaseDir, "assets", Path.GetRelativePath(assets, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(file, to, true);
        }
        var host = new CancelHost(); Host.Use(host); Host.LogSink = Console.WriteLine;
        PlatformGameBridge.Notice = text => Console.WriteLine("NOTICE " + text);
        int code = GameSession.Start(settings, Console.WriteLine);
        if (code != 0 || host.AskCount == 0 || host.GraphicsCalls != 0)
            throw new InvalidDataException("Actual Main should reach the original-folder prompt and return after cancel: code=" + code + ", asks=" + host.AskCount + ", graphics=" + host.GraphicsCalls);
        string report = File.ReadAllText(Path.Combine(root, "game-start.txt"));
        if (!report.Contains("Main invoked: yes") || !report.Contains("Main returned normally")) throw new InvalidDataException("Main execution was not recorded");
        File.Copy(Path.Combine(root, "game-start.txt"), Path.Combine(output, "game-start.txt"), true);
        Console.WriteLine("PASS ACTUAL GAME MAIN: original-folder dialog reached; cancellation returned normally; no GPU/gameplay claimed");
        var game = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "WarOfGenesis");
        var reportType = game.GetType("DuelDx.MyBugReportsDialog", true)!;
        var shot = reportType.GetMethod("ShotIfAsked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        if (!Equals(shot.Invoke(null, null), false)) throw new InvalidDataException("Report screenshot hook still runs");
        Console.WriteLine("PASS rewritten startup report hook executes and returns false");
        try { PlatformGameBridge.Exit(7); } catch (GameExitException ex) when (ex.ExitCode == 7) { Console.WriteLine("PASS controlled game exit preserves the host process"); }
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
