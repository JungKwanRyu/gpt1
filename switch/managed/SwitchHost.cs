using System.Collections.Concurrent;
using WogPatcher;
using WogShim;
using WogShim.Win32;

namespace WogSwitch;

internal sealed class SwitchHost(bool desktop) : IHost
{
    private readonly int uiThread = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<DialogRequest> requests = new();
    private bool gameActive;
    internal LauncherSettings? Settings { get; set; }
    internal int Frames { get; private set; }
    internal bool ExitRequested { get; private set; }
    public (int W, int H) SurfaceSize => desktop ? (1280, 720) : (Native.wog_width(), Native.wog_height());
    public bool IsGles => !desktop && Native.wog_is_gles() != 0;
    public bool ListDrives => false;
    public bool HasTouch => false;
    public nint GetProcAddress(string name) => Native.wog_gl_proc(name);

    internal void BeginGame() { Frames = 0; Volatile.Write(ref gameActive, true); }
    internal void EndGame()
    {
        Volatile.Write(ref gameActive, false);
        while (requests.TryDequeue(out var request)) request.Done.TrySetResult(-1);
    }

    public void SwapBuffers()
    {
        Native.wog_swap();
        if (gameActive && ++Frames == 1) Program.Log("PASS FIRST GAME FRAME PRESENTED through WogShim host (visual correctness unverified)");
    }

    public void PumpEvents()
    {
        if (desktop) return;
        if (Native.wog_running() == 0)
        {
            ExitRequested = true; User32.PostQuitMessage(0); return;
        }
        while (requests.TryDequeue(out var request))
            if (!request.Done.Task.IsCompleted) request.Done.TrySetResult(ShowDialog(request.Caption, request.Text, request.Buttons));
        GameHooks.Refresh(); GameState.Refresh();
    }

    public void ShowMessage(string caption, string text) => Ask(caption, text, new[] { "확인" });

    public int Ask(string caption, string text, string[] buttons)
    {
        Program.Log("[dialog] " + caption + ": " + text + " / " + string.Join(" | ", buttons));
        if (desktop) return buttons.Length - 1;
        if (Environment.CurrentManagedThreadId == uiThread) return ShowDialog(caption, text, buttons);
        if (!Volatile.Read(ref gameActive)) return -1;
        var request = new DialogRequest(caption, text, buttons, new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously));
        requests.Enqueue(request);
        while (!request.Done.Task.Wait(50))
            if (!Volatile.Read(ref gameActive)) request.Done.TrySetResult(-1);
        return request.Done.Task.Result;
    }

    private int ShowDialog(string caption, string text, string[] buttons)
    {
        if (buttons.Length == 0) buttons = new[] { "확인" };
        string font = Path.Combine(Settings?.Root ?? "/switch/WogSwitchProbe", "assets/NanumGothic.ttf");
        if (Native.wog_dialog_begin() == 0) throw new InvalidOperationException("Cannot preserve game graphics state for dialog");
        try
        {
            int selected = 0, previous = 0, drawn = -1;
            bool released = false;
            while (Native.wog_running() != 0)
            {
                var pad = ReadPad(); int now = pad.Buttons;
                if (now == 0) released = true;
                int down = released ? now & ~previous : 0; previous = now;
                if ((down & (0x0001 | 0x0004)) != 0) selected = (selected + buttons.Length - 1) % buttons.Length;
                if ((down & (0x0002 | 0x0008)) != 0) selected = (selected + 1) % buttons.Length;
                if ((down & 0x1000) != 0) return selected;
                if ((down & 0x2000) != 0) return -1;
                if (selected != drawn)
                {
                    using var panel = DialogPanel.Create(font, caption, text, buttons, selected);
                    if (Native.wog_upload_bitmap(panel.GetPixels(), panel.Width, panel.Height, panel.RowBytes) == 0)
                        throw new InvalidOperationException("Dialog bitmap upload failed");
                    drawn = selected;
                }
                Native.wog_draw_bitmap(); Native.wog_swap();
            }
            ExitRequested = true; User32.PostQuitMessage(0); return -1;
        }
        finally { Native.wog_dialog_end(); }
    }

    public string PickFolder(string description)
    {
        string? folder = description.Contains("파트1") ? (Settings?.Part1Present == true ? Settings.OriginalPart1 : null) :
            (Settings?.Part2Present == true ? Settings.OriginalPart2 : null);
        if (folder == null) ShowMessage("원본 폴더 설정", "launcher.ini의 original(파트2) 또는 part1(파트1) 경로에 원본 자료를 넣고 앱을 다시 실행해 주세요.");
        return folder!;
    }

    public IAudioSink OpenAudio(int sampleRate, int channels, int bitsPerSample)
    {
        Program.Log("AUDIO NOT READY: " + sampleRate + " Hz / " + channels + " channels / " + bitsPerSample + " bits");
        throw new NotSupportedException("Switch PCM audio output is not integrated in checkpoint 0.5.");
    }

    internal XInput.State ReadPad()
    {
        Native.wog_pad(out Native.Pad p);
        return new XInput.State { PacketNumber = p.Packet, Buttons = p.Buttons,
            LeftTrigger = p.LeftTrigger, RightTrigger = p.RightTrigger,
            ThumbLX = p.LX, ThumbLY = p.LY, ThumbRX = p.RX, ThumbRY = p.RY };
    }

    private sealed record DialogRequest(string Caption, string Text, string[] Buttons, TaskCompletionSource<int> Done);
}
