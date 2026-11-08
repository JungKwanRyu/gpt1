using System.Runtime.InteropServices;
using WogShim;

namespace WogSwitch;

internal interface IPcmDevice
{
    int Open(int rate, int channels, int bits);
    bool Write(int token, ReadOnlySpan<byte> pcm);
    long Played(int token);
    bool Flush(int token);
    void Close(int token);
}

internal sealed class SwitchAudioSink : IAudioSink
{
    private readonly object gate = new();
    private readonly IPcmDevice device;
    private readonly int token, frameBytes;
    private bool closed;
    private long lastPlayed;

    private SwitchAudioSink(IPcmDevice device, int token, int frameBytes)
    { this.device = device; this.token = token; this.frameBytes = frameBytes; }

    internal static IAudioSink? Open(int rate, int channels, int bits, IPcmDevice? device = null)
    {
        if (rate < 8000 || rate > 192000 || channels is not (1 or 2) || bits is not (8 or 16))
        { Host.Log("AUDIO OPEN rejected unsupported PCM format"); return null; }
        device ??= NativePcmDevice.Instance;
        int token = device.Open(rate, channels, bits);
        if (token == 0) { Host.Log("AUDIO OPEN FAILED: " + rate + " Hz / " + channels + " channels / " + bits + " bits; device unavailable (no audio output)"); return null; }
        return new SwitchAudioSink(device, token, channels * bits / 8);
    }

    public long PlayedFrames
    {
        get { lock (gate) { if (!closed) lastPlayed = Math.Max(lastPlayed, device.Played(token)); return lastPlayed; } }
    }

    public void Write(ReadOnlySpan<byte> pcm)
    {
        lock (gate)
        {
            if (closed || pcm.IsEmpty) return;
            if (pcm.Length % frameBytes != 0 || !device.Write(token, pcm)) Disable("PCM write failed or incomplete input frame");
        }
    }

    public void Flush()
    {
        lock (gate) if (!closed && !device.Flush(token)) Disable("PCM flush failed");
    }

    private void Disable(string reason)
    {
        device.Close(token); closed = true;
        Host.Log("AUDIO OUTPUT STOPPED: " + reason + "; no exception is raised on the mixer thread");
    }

    public void Dispose()
    {
        lock (gate) if (!closed) { device.Close(token); closed = true; }
    }

    private sealed class NativePcmDevice : IPcmDevice
    {
        internal static readonly NativePcmDevice Instance = new();
        public int Open(int rate, int channels, int bits) => wog_audio_open(rate, channels, bits);
        public unsafe bool Write(int token, ReadOnlySpan<byte> pcm)
        { fixed (byte* bytes = pcm) return wog_audio_write(token, (nint)bytes, pcm.Length) != 0; }
        public long Played(int token) => wog_audio_played(token);
        public bool Flush(int token) => wog_audio_flush(token) != 0;
        public void Close(int token) => wog_audio_close(token);

        [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)] private static extern int wog_audio_open(int rate, int channels, int bits);
        [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)] private static extern int wog_audio_write(int token, nint bytes, int length);
        [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)] private static extern long wog_audio_played(int token);
        [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)] private static extern int wog_audio_flush(int token);
        [DllImport("WogSwitch", CallingConvention = CallingConvention.Cdecl)] private static extern void wog_audio_close(int token);
    }
}
