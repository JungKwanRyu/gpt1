#include "audio_pcm.h"
#include <string.h>

int wog_pcm_init(WogPcm *s, uint32_t input_rate, uint32_t output_rate, uint32_t channels, uint32_t bits)
{
    if (!s || input_rate < 8000 || input_rate > 192000 || output_rate != 48000 || (channels != 1 && channels != 2) || (bits != 8 && bits != 16)) return 0;
    memset(s, 0, sizeof(*s));
    s->input_rate = input_rate; s->output_rate = output_rate; s->channels = channels;
    s->bits = bits;
    return 1;
}

static int16_t blend(int16_t a, int16_t b, uint64_t fraction, uint32_t rate)
{
    int64_t value = (int64_t)a * (int64_t)(rate - fraction) + (int64_t)b * (int64_t)fraction;
    return (int16_t)(value >= 0 ? (value + rate / 2) / rate : -((-value + rate / 2) / rate));
}

int wog_pcm_write(WogPcm *s, const uint8_t *bytes, size_t count, WogPcmEmit emit, void *context)
{
    if (!s || !s->input_rate || !emit || (!bytes && count) || count % (s->channels * (s->bits / 8))) return 0;
    size_t sample_bytes = s->bits / 8, frame_bytes = s->channels * sample_bytes;
    for (size_t offset = 0; offset < count; offset += frame_bytes) {
        int16_t current[2];
        for (uint32_t ch = 0; ch < s->channels; ch++) {
            size_t p = offset + ch * sample_bytes;
            current[ch] = s->bits == 8 ? (int16_t)(((int)bytes[p] - 128) * 256) : (int16_t)((uint16_t)bytes[p] | (uint16_t)bytes[p + 1] << 8);
        }
        if (s->channels == 1) current[1] = current[0];
        uint64_t boundary = s->input_index * s->output_rate;
        if (s->input_index == 0) {
            if (!emit(context, current[0], current[1])) return 0;
            s->next_time = s->input_rate;
        } else {
            while (s->next_time <= boundary) {
                uint64_t fraction = s->next_time - (s->input_index - 1) * s->output_rate;
                if (!emit(context, blend(s->previous[0], current[0], fraction, s->output_rate),
                           blend(s->previous[1], current[1], fraction, s->output_rate))) return 0;
                s->next_time += s->input_rate;
            }
        }
        s->previous[0] = current[0]; s->previous[1] = current[1];
        s->input_index++;
    }
    return 1;
}
