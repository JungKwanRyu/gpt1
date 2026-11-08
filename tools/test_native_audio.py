#!/usr/bin/env python3
"""Test the real streaming PCM converter and audout driver against a service mock."""
import argparse
import ctypes as c
import math
from pathlib import Path
import struct
import subprocess
import tempfile
import wave

HEADER = r'''
#pragma once
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
typedef uint32_t Result;
#define R_FAILED(x) ((x) != 0)
#define R_SUCCEEDED(x) ((x) == 0)
typedef enum { PcmFormat_Int16 = 2 } PcmFormat;
typedef struct AudioOutBuffer { struct AudioOutBuffer *next; void *buffer; uint64_t buffer_size, data_size, data_offset; } AudioOutBuffer;
Result audoutInitialize(void); void audoutExit(void);
uint32_t audoutGetSampleRate(void); uint32_t audoutGetChannelCount(void); PcmFormat audoutGetPcmFormat(void);
Result audoutStartAudioOut(void); Result audoutStopAudioOut(void);
Result audoutAppendAudioOutBuffer(AudioOutBuffer *);
Result audoutGetReleasedAudioOutBuffer(AudioOutBuffer **, uint32_t *);
Result audoutWaitPlayFinish(AudioOutBuffer **, uint32_t *, uint64_t);
Result audoutFlushAudioOutBuffers(bool *);
Result audoutContainsAudioOutBuffer(AudioOutBuffer *, bool *);
void armDCacheFlush(void *, uint64_t);
'''
MOCK = r'''
#include "audio_test_platform.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
static AudioOutBuffer *queue[4], *released[4];
static int queued, releases, opened, started;
int mock_failure, mock_waits, mock_exits, mock_max_queue, mock_appends, mock_flushes;
static unsigned char output[4 * 48000 * 8];
static int output_size;
void wog_log(const char *text) { (void)text; }
void mock_reset(void) { assert(!opened); queued = releases = 0; mock_failure = mock_waits = mock_exits = mock_max_queue = mock_appends = mock_flushes = output_size = 0; }
int mock_output_size(void) { return output_size; }
void mock_copy_output(void *to) { memcpy(to, output, output_size); }
Result audoutInitialize(void) { if (mock_failure == 1) return 1; opened = 1; return 0; }
void audoutExit(void) { opened = started = queued = releases = 0; mock_exits++; }
uint32_t audoutGetSampleRate(void) { return 48000; }
uint32_t audoutGetChannelCount(void) { return 2; }
PcmFormat audoutGetPcmFormat(void) { return PcmFormat_Int16; }
Result audoutStartAudioOut(void) { if (mock_failure == 2) return 2; assert(opened); started = 1; return 0; }
Result audoutStopAudioOut(void) { started = 0; return 0; }
void armDCacheFlush(void *p, uint64_t n) { assert((uintptr_t)p % 4096 == 0 && n == 4096); mock_flushes++; }
Result audoutAppendAudioOutBuffer(AudioOutBuffer *buffer) {
    if (mock_failure == 3) return 3;
    assert(opened && started && queued < 4 && mock_flushes > mock_appends);
    assert((uintptr_t)buffer->buffer % 4096 == 0 && buffer->buffer_size == 4096 && buffer->data_size == 960 * 4 && buffer->data_offset == 0);
    for (int i = 0; i < queued; i++) assert(queue[i] != buffer);
    assert(output_size + buffer->data_size <= sizeof(output));
    memcpy(output + output_size, buffer->buffer, buffer->data_size); output_size += (int)buffer->data_size;
    queue[queued++] = buffer; mock_appends++; if (queued > mock_max_queue) mock_max_queue = queued;
    return 0;
}
Result audoutGetReleasedAudioOutBuffer(AudioOutBuffer **buffer, uint32_t *count) {
    *count = 0; *buffer = NULL;
    if (releases) { *buffer = released[0]; *count = 1; memmove(released, released + 1, --releases * sizeof(*released)); }
    return 0;
}
Result audoutWaitPlayFinish(AudioOutBuffer **buffer, uint32_t *count, uint64_t timeout) {
    assert(timeout == 100000000ULL); mock_waits++;
    if (mock_failure == 4) return 4;
    *count = 0; *buffer = NULL;
    if (queued) { *buffer = queue[0]; *count = 1; memmove(queue, queue + 1, --queued * sizeof(*queue)); }
    return 0;
}
Result audoutFlushAudioOutBuffers(bool *flushed) {
    if (mock_failure == 5) return 5;
    assert(!started && !releases); *flushed = queued > 0;
    memcpy(released, queue, queued * sizeof(*queue)); releases = queued; queued = 0;
    return 0;
}
Result audoutContainsAudioOutBuffer(AudioOutBuffer *buffer, bool *contains) {
    *contains = false; for (int i = 0; i < queued; i++) if (queue[i] == buffer) *contains = true;
    return 0;
}
'''


class Pcm(c.Structure):
    _fields_ = [(n, c.c_uint32) for n in ["input_rate", "output_rate", "channels", "bits"]] + [(n, c.c_uint64) for n in ["input_index", "next_time"]] + [("previous", c.c_int16 * 2)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    with tempfile.TemporaryDirectory(prefix="wog-audio-") as temporary:
        folder = Path(temporary)
        (folder / "audio_test_platform.h").write_text(HEADER)
        (folder / "mock.c").write_text(MOCK)
        subprocess.run(["gcc", "-std=gnu17", "-O1", "-g", "-Wall", "-Wextra", "-Werror", "-Wno-sign-compare", "-shared", "-fPIC", "-pthread", "-fsanitize=undefined", "-fno-sanitize-recover=undefined", "-DWOG_AUDIO_TEST", "-I" + str(folder), str(root / "switch/runtime/audio.c"), str(root / "switch/runtime/audio_pcm.c"), str(folder / "mock.c"), "-o", str(folder / "audio.so")], check=True)
        library = c.CDLL(str(folder / "audio.so"))
        emit_type = c.CFUNCTYPE(c.c_int, c.c_void_p, c.c_int16, c.c_int16)
        library.wog_pcm_init.argtypes = [c.POINTER(Pcm)] + [c.c_uint32] * 4
        library.wog_pcm_write.argtypes = [c.POINTER(Pcm), c.c_void_p, c.c_size_t, emit_type, c.c_void_p]
        library.wog_audio_open.argtypes = [c.c_int] * 3
        library.wog_audio_write.argtypes = [c.c_int, c.c_void_p, c.c_int]
        library.wog_audio_played.argtypes = [c.c_int]; library.wog_audio_played.restype = c.c_int64
        library.wog_audio_flush.argtypes = [c.c_int]
        library.wog_audio_close.argtypes = [c.c_int]
        def variable(name): return c.c_int.in_dll(library, name)
        def convert(data, rate=44100, channels=2, bits=16, chunk=44100):
            state = Pcm(); assert library.wog_pcm_init(c.byref(state), rate, 48000, channels, bits)
            frames = []
            @emit_type
            def emit(_, left, right): frames.append((left, right)); return 1
            step = chunk * channels * (bits // 8)
            for start in range(0, len(data), step):
                block = data[start:start + step]
                assert library.wog_pcm_write(c.byref(state), c.c_char_p(block), len(block), emit, None)
            return frames
        samples = [(int(24000 * math.sin(2 * math.pi * 440 * i / 44100)), int(12000 * math.cos(2 * math.pi * 440 * i / 44100))) for i in range(44100)]
        data = b"".join(struct.pack("<hh", *f) for f in samples)
        output = convert(data)
        assert len(output) == (44100 - 1) * 48000 // 44100 + 1
        for chunk in [1, 17, 441, 4096]: assert convert(data, chunk=chunk) == output
        assert max(abs(f[0] - 24000 * math.sin(2 * math.pi * 440 * i / 48000)) for i, f in enumerate(output)) < 14
        assert max(abs(f[1] - 12000 * math.cos(2 * math.pi * 440 * i / 48000)) for i, f in enumerate(output)) < 8
        print("PASS 44100->48000 PCM stereo frame count, channel separation, sine accuracy and exact continuity across 1/17/441/4096-frame writes")
        assert convert(data, rate=48000) == samples
        assert convert(bytes([0, 128, 255]), rate=48000, channels=1, bits=8) == [(-32768, -32768), (0, 0), (32512, 32512)]
        state = Pcm(); assert not library.wog_pcm_init(c.byref(state), 44100, 48000, 3, 16)
        print("PASS 48000 Hz identity, unsigned 8-bit mono conversion and unsupported format refusal")
        library.mock_reset()
        token = library.wog_audio_open(44100, 2, 16); assert token > 0
        assert library.wog_audio_open(48000, 2, 16) == 0
        assert library.wog_audio_write(token + 1, c.c_char_p(data), len(data)) == 0
        assert library.wog_audio_write(token, c.c_char_p(data), len(data)) == 1
        assert variable("mock_waits").value > 0 and variable("mock_max_queue").value == 4
        assert 0 < library.wog_audio_played(token) <= 44100
        assert library.wog_audio_flush(token) == 1
        assert library.wog_audio_played(token) == 44100
        assert library.wog_audio_write(token, c.c_char_p(data[:8000]), 8000) == 1
        library.wog_audio_close(token); assert variable("mock_exits").value == 1
        newer = library.wog_audio_open(48000, 1, 8); assert newer != token
        library.wog_audio_close(token)
        assert library.wog_audio_write(newer, c.c_char_p(bytes([128]) * 2000), 2000) == 1
        library.wog_audio_shutdown()
        print("PASS real audout driver buffer alignment/flush/ownership, bounded queue pacing, frame clock, reset, stale-token protection and shutdown (mock service)")
        for failure in [1, 2, 3, 4, 5]:
            library.mock_reset(); variable("mock_failure").value = failure
            token = library.wog_audio_open(44100, 2, 16)
            if failure in [1, 2]: assert token == 0; continue
            if failure in [3, 4]: assert library.wog_audio_write(token, c.c_char_p(data), len(data)) == 0
            else: assert library.wog_audio_flush(token) == 0
            assert variable("mock_exits").value == 1
            library.wog_audio_close(token)
        print("PASS initialization/start/append/wait/flush failure cleanup without reusing driver-owned DMA buffers; UBSan enabled")
        if args.output:
            args.output.mkdir(parents=True, exist_ok=True)
            with wave.open(str(args.output / "synthetic-440hz-48k.wav"), "wb") as sound:
                sound.setnchannels(2); sound.setsampwidth(2); sound.setframerate(48000)
                sound.writeframes(b"".join(struct.pack("<hh", *frame) for frame in output))


if __name__ == "__main__": main()
