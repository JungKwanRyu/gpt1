#pragma once
#include <stdint.h>

int wog_audio_open(int sample_rate, int channels, int bits);
int wog_audio_write(int token, const void *data, int bytes);
int64_t wog_audio_played(int token);
int wog_audio_flush(int token);
void wog_audio_close(int token);
void wog_audio_shutdown(void);
