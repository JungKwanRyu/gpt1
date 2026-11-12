#pragma once
#include <stddef.h>
#include <stdint.h>

typedef struct {
    uint32_t input_rate, output_rate, channels, bits;
    uint64_t input_index, next_time;
    int16_t previous[2];
} WogPcm;

typedef int (*WogPcmEmit)(void *context, int16_t left, int16_t right);
int wog_pcm_init(WogPcm *state, uint32_t input_rate, uint32_t output_rate, uint32_t channels, uint32_t bits);
int wog_pcm_write(WogPcm *state, const uint8_t *bytes, size_t count, WogPcmEmit emit, void *context);

int wog_mix_voice(const int16_t *samples, int frames, int channels, double step,
    float left, float right, int loop, double *position, int *laps, int32_t *mix, int output_frames);
int wog_audio_rdft(float *x, double *re, double *im, const int *rev, const double *cosine, const double *sine, int n);
