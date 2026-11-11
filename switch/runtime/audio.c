#ifdef WOG_AUDIO_TEST
#include "audio_test_platform.h"
#else
#include <switch.h>
#endif
#include "audio.h"
#include "audio_pcm.h"
#include <malloc.h>
#include <pthread.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <limits.h>

extern void wog_log(const char *message);
enum { BLOCKS = 4, BLOCK_FRAMES = 960, BLOCK_BYTES = 4096 };
static pthread_mutex_t gate = PTHREAD_MUTEX_INITIALIZER;
static struct {
    bool service, active, queued[BLOCKS], first_release;
    int token, serial, write_index, filled;
    uint64_t input_total, played_base, released_output, submitted;
    AudioOutBuffer buffers[BLOCKS];
    WogPcm pcm;
} audio;

static void error(const char *operation, Result result)
{
    char line[192];
    snprintf(line, sizeof(line), "AUDIO ERROR %s: 0x%08x", operation, (unsigned)result);
    wog_log(line);
}

static void close_locked(void)
{
    audio.active = false;
    if (audio.service) {
        audoutStopAudioOut();
        // Closing the session releases server ownership before DMA memory is freed.
        audoutExit();
        audio.service = false;
    }
    for (int i = 0; i < BLOCKS; i++) {
        free(audio.buffers[i].buffer);
        memset(&audio.buffers[i], 0, sizeof(audio.buffers[i]));
        audio.queued[i] = false;
    }
    audio.filled = audio.write_index = 0;
}

static int release_one(AudioOutBuffer *released, uint32_t count)
{
    if (!count) return 1;
    for (int i = 0; i < BLOCKS; i++) {
        if (released == &audio.buffers[i] && audio.queued[i]) {
            audio.queued[i] = false;
            audio.released_output += BLOCK_FRAMES;
            if (!audio.first_release) { audio.first_release = true; wog_log("PASS audio PCM buffer released by audout; audible output still requires device confirmation"); }
            return 1;
        }
    }
    wog_log("AUDIO ERROR unexpected released buffer tag");
    return 0;
}

static int reclaim(void)
{
    for (int i = 0; i <= BLOCKS; i++) {
        AudioOutBuffer *released = NULL; uint32_t count = 0;
        Result rc = audoutGetReleasedAudioOutBuffer(&released, &count);
        if (R_FAILED(rc)) { error("get released buffer", rc); return 0; }
        if (!count) return 1;
        if (!release_one(released, count)) return 0;
    }
    return 0;
}

static int ensure_writable(void)
{
    if (!audio.queued[audio.write_index]) return 1;
    if (!reclaim()) return 0;
    for (int tries = 0; audio.queued[audio.write_index] && tries < 20; tries++) {
        AudioOutBuffer *released = NULL; uint32_t count = 0;
        Result rc = audoutWaitPlayFinish(&released, &count, 100000000ULL);
        if (R_FAILED(rc)) { error("wait playback", rc); return 0; }
        if (!release_one(released, count)) return 0;
    }
    if (audio.queued[audio.write_index]) { wog_log("AUDIO ERROR no buffer released within bounded wait"); return 0; }
    return 1;
}

#ifndef WOG_AUDIO_TEST
static uint64_t last_submit_tick, longest_submit_gap, diagnostic_tick;
#endif
static int emit(void *unused, int16_t left, int16_t right)
{
    (void)unused;
    if (!ensure_writable()) return 0;
    AudioOutBuffer *buffer = &audio.buffers[audio.write_index];
    int16_t *samples = buffer->buffer;
    samples[audio.filled * 2] = left; samples[audio.filled * 2 + 1] = right;
    if (++audio.filled != BLOCK_FRAMES) return 1;
    buffer->data_size = BLOCK_FRAMES * 4;
    armDCacheFlush(buffer->buffer, buffer->buffer_size);
    Result rc = audoutAppendAudioOutBuffer(buffer);
    if (R_FAILED(rc)) { error("append buffer", rc); return 0; }
    audio.queued[audio.write_index] = true;
    #ifndef WOG_AUDIO_TEST
    uint64_t tick=armGetSystemTick();
    if(last_submit_tick && tick-last_submit_tick>longest_submit_gap) longest_submit_gap=tick-last_submit_tick;
    last_submit_tick=tick;
    if(!diagnostic_tick) diagnostic_tick=tick;
    if(tick-diagnostic_tick>armGetSystemTickFreq()*5) {
        char line[192]; int queued=0; for(int j=0;j<BLOCKS;j++) queued+=audio.queued[j];
        snprintf(line,sizeof(line),"AUDIO supply max-submit-gap=%.1fms queued=%d/4 submitted=%llu",longest_submit_gap*1000.0/armGetSystemTickFreq(),queued,(unsigned long long)audio.submitted);
        wog_log(line);longest_submit_gap=0;diagnostic_tick=tick;
    }
    #endif
    if (++audio.submitted == 1) wog_log("PASS audio PCM buffer submitted to audout");
    audio.write_index = (audio.write_index + 1) % BLOCKS;
    audio.filled = 0;
    return 1;
}

int wog_audio_open(int sample_rate, int channels, int bits)
{
    pthread_mutex_lock(&gate);
    int token = 0;
    if (audio.active) { wog_log("AUDIO OPEN rejected: another PCM output is active"); goto done; }
    if (!wog_pcm_init(&audio.pcm, (uint32_t)sample_rate, 48000, (uint32_t)channels, (uint32_t)bits)) {
        wog_log("AUDIO OPEN rejected: supported input is 8/16-bit PCM, 1/2 channels, 8000..192000 Hz"); goto done;
    }
    Result rc = audoutInitialize();
    if (R_FAILED(rc)) { error("initialize", rc); goto done; }
    audio.service = true;
    if (audoutGetSampleRate() != 48000 || audoutGetChannelCount() != 2 || audoutGetPcmFormat() != PcmFormat_Int16) {
        wog_log("AUDIO ERROR unexpected device PCM format"); close_locked(); goto done;
    }
    for (int i = 0; i < BLOCKS; i++) {
        audio.buffers[i].buffer = memalign(4096, BLOCK_BYTES);
        if (!audio.buffers[i].buffer) { wog_log("AUDIO ERROR DMA allocation failed"); close_locked(); goto done; }
        memset(audio.buffers[i].buffer, 0, BLOCK_BYTES);
        audio.buffers[i].buffer_size = BLOCK_BYTES;
    }
    rc = audoutStartAudioOut();
    if (R_FAILED(rc)) { error("start", rc); close_locked(); goto done; }
    audio.input_total = audio.played_base = audio.released_output = audio.submitted = 0;
    audio.first_release = false;
    audio.active = true;
    if (audio.serial == INT_MAX) audio.serial = 0;
    audio.token = ++audio.serial;
    token = audio.token;
    char line[192];
    snprintf(line, sizeof(line), "PASS audio audout opened: %d Hz/%d ch/%d-bit -> 48000 Hz/2 ch/16-bit; 4 x 20 ms DMA buffers", sample_rate, channels, bits);
    wog_log(line);
done:
    pthread_mutex_unlock(&gate);
    return token;
}

int wog_audio_write(int token, const void *data, int bytes)
{
    pthread_mutex_lock(&gate);
    int ok = audio.active && token == audio.token && bytes >= 0 &&
        wog_pcm_write(&audio.pcm, data, (size_t)bytes, emit, NULL);
    if (ok) audio.input_total += (uint64_t)bytes / (audio.pcm.channels * (audio.pcm.bits / 8));
    else if (audio.active && token == audio.token) close_locked();
    pthread_mutex_unlock(&gate);
    return ok;
}

int64_t wog_audio_played(int token)
{
    pthread_mutex_lock(&gate);
    uint64_t result = 0;
    if (audio.active && token == audio.token) {
        if (!reclaim()) close_locked();
        result = audio.played_base + audio.released_output * audio.pcm.input_rate / 48000;
        if (result > audio.input_total) result = audio.input_total;
    }
    pthread_mutex_unlock(&gate);
    return (int64_t)result;
}

int wog_audio_flush(int token)
{
    pthread_mutex_lock(&gate);
    int ok = 0;
    if (!audio.active || token != audio.token) goto done;
    bool flushed = false;
    Result rc = audoutStopAudioOut();
    if (R_SUCCEEDED(rc)) rc = audoutFlushAudioOutBuffers(&flushed);
    if (R_FAILED(rc)) { error("flush", rc); close_locked(); goto done; }
    if (!reclaim()) { close_locked(); goto done; }
    for (int i = 0; i < BLOCKS; i++) {
        bool contains = false;
        rc = audoutContainsAudioOutBuffer(&audio.buffers[i], &contains);
        if (R_FAILED(rc) || contains) { wog_log("AUDIO ERROR flush retained a driver-owned buffer"); close_locked(); goto done; }
        audio.queued[i] = false;
    }
    audio.played_base = audio.input_total; audio.released_output = 0;
    audio.filled = audio.write_index = 0;
    wog_pcm_init(&audio.pcm, audio.pcm.input_rate, 48000, audio.pcm.channels, audio.pcm.bits);
    rc = audoutStartAudioOut();
    if (R_FAILED(rc)) { error("restart", rc); close_locked(); goto done; }
    ok = 1;
done:
    pthread_mutex_unlock(&gate);
    return ok;
}

void wog_audio_close(int token)
{
    pthread_mutex_lock(&gate);
    if (audio.active && token == audio.token) close_locked();
    pthread_mutex_unlock(&gate);
}

void wog_audio_shutdown(void)
{
    pthread_mutex_lock(&gate);
    close_locked();
    pthread_mutex_unlock(&gate);
}
