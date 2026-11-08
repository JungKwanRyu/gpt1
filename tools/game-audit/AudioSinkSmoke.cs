using WogSwitch;

internal static class AudioSinkSmoke
{
    internal static void Run()
    {
        var device = new FakeDevice();
        using (var sink = SwitchAudioSink.Open(44100, 2, 16, device)!)
        {
            sink.Write(new byte[400]);
            if (device.Writes != 1 || sink.PlayedFrames != 20) throw new InvalidDataException("PCM sink did not forward data/clock");
            device.Position = 10;
            if (sink.PlayedFrames != 20) throw new InvalidDataException("Audio clock moved backwards");
            sink.Flush(); if (device.Flushes != 1) throw new InvalidDataException("Audio flush was not forwarded");
            device.FailWrite = true; sink.Write(new byte[4]);
            sink.Write(new byte[4]); sink.Flush();
            if (device.Closes != 1 || device.Writes != 2) throw new InvalidDataException("Mixer-thread failure must close the sink once and stop writing");
        }
        if (device.Closes != 1) throw new InvalidDataException("Audio Dispose closed an already closed sink twice");
        if (SwitchAudioSink.Open(44100, 2, 16, new FakeDevice { OpenToken = 0 }) != null) throw new InvalidDataException("Unavailable output must be reported as unavailable");
        if (SwitchAudioSink.Open(44100, 3, 24, new FakeDevice()) != null) throw new InvalidDataException("Invalid format accepted");
        using (var sink = SwitchAudioSink.Open(48000, 1, 8, new FakeDevice())!) sink.Write(new byte[] { 0, 128, 255 });
        Console.WriteLine("PASS managed IAudioSink PCM forwarding, played clock, flush, failure containment, single disposal, 8-bit input and unavailable-device reporting (fake interop)");
    }

    private sealed class FakeDevice : IPcmDevice
    {
        internal int OpenToken = 7, Writes, Flushes, Closes;
        internal long Position = 20;
        internal bool FailWrite;
        public int Open(int rate, int channels, int bits) => OpenToken;
        public bool Write(int token, ReadOnlySpan<byte> pcm) { Writes++; return !FailWrite; }
        public long Played(int token) => Position;
        public bool Flush(int token) { Flushes++; return true; }
        public void Close(int token) => Closes++;
    }
}
