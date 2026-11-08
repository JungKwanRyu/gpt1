using WogShim;
using WogSwitch;

internal static class RealMixerSmoke
{
    internal static void Run()
    {
        using var received = new ManualResetEventSlim();
        var host = new AudioHost(received); Host.Use(host);
        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "WarOfGenesis.Assets");
        Type type = assembly.GetType("WarOfGenesis.Assets.AudioMixer", true)!;
        using (var mixer = (IDisposable)Activator.CreateInstance(type)!)
        {
            if (!received.Wait(5000) || host.Rate != 44100 || host.Channels != 2 || host.Bits != 16 || host.Device.Writes == 0)
                throw new InvalidDataException("Actual game AudioMixer did not deliver 44.1kHz PCM through the real WogShim to IAudioSink");
        }
        if (host.Device.Closes == 0) throw new InvalidDataException("Actual game mixer did not release its audio sink");
        Console.WriteLine("PASS actual patched game AudioMixer -> WogShim waveOut -> SwitchAudioSink delivers 44100 Hz stereo 16-bit PCM and disposes output (fake native interop)");
    }

    private sealed class Device(ManualResetEventSlim received) : IPcmDevice
    {
        internal int Writes, Closes;
        private long frames;
        public int Open(int rate, int channels, int bits) => 1;
        public bool Write(int token, ReadOnlySpan<byte> pcm)
        { Interlocked.Increment(ref Writes); Interlocked.Add(ref frames, pcm.Length / 4); received.Set(); return true; }
        public long Played(int token) => Interlocked.Read(ref frames);
        public bool Flush(int token) => true;
        public void Close(int token) => Interlocked.Increment(ref Closes);
    }

    private sealed class AudioHost(ManualResetEventSlim received) : IHost
    {
        internal readonly Device Device = new(received);
        internal int Rate, Channels, Bits;
        public IAudioSink OpenAudio(int rate, int channels, int bits)
        { Rate = rate; Channels = channels; Bits = bits; return SwitchAudioSink.Open(rate, channels, bits, Device)!; }
        public (int W, int H) SurfaceSize => (1280, 720);
        public bool IsGles => true;
        public bool ListDrives => false;
        public bool HasTouch => false;
        public nint GetProcAddress(string name) => throw new InvalidOperationException("Audio smoke must not access GPU");
        public void SwapBuffers() => throw new InvalidOperationException("Audio smoke must not render");
        public void PumpEvents() { }
        public void ShowMessage(string caption, string text) { }
        public int Ask(string caption, string text, string[] buttons) => buttons.Length - 1;
        public string PickFolder(string description) => null!;
    }
}
