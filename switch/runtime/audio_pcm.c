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
        int16_t current[2] = { 0, 0 };
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

#include <math.h>

/* Float temporaries prevent a target-dependent fused multiply-add from changing
   the PCM bits relative to the original managed sample interpolation. */
static int32_t voice_sample(int16_t a, int16_t b, float fraction, float gain)
{
    volatile float delta = ((float)b - (float)a) * fraction;
    volatile float value = (float)a + delta;
    volatile float scaled = value * gain;
    return (int32_t)scaled;
}

int wog_mix_voice(const int16_t *samples, int frames, int channels, double step,
    float left, float right, int loop, double *position, int *laps, int32_t *mix, int output_frames)
{
    if (!samples || !position || !laps || !mix || frames <= 0 || (channels != 1 && channels != 2) ||
        !isfinite(step) || step <= 0 || step > 16 || !isfinite(*position) || *position < 0 || *position > 1e12 ||
        output_frames < 0 || output_frames > 1764) return 0;
    if (!isfinite(left) || !isfinite(right)) left = right = 0;
    if (fabsf(left) > 8 || fabsf(right) > 8) return 0;
    double pos = *position;
    for (int i = 0; i < output_frames; i++) {
        int64_t index = (int64_t)pos;
        if (index >= frames) {
            if (!loop) { *position = pos; return 0; }
            pos -= frames * floor(pos / frames);
            *laps = (int32_t)((uint32_t)*laps + 1u);
            index = (int64_t)pos;
        }
        int64_t next = index + 1 < frames ? index + 1 : loop ? 0 : index;
        float fraction = (float)(pos - index);
        int32_t l = voice_sample(samples[index * channels], samples[next * channels], fraction, left);
        int32_t r = voice_sample(samples[index * channels + (channels == 2)], samples[next * channels + (channels == 2)], fraction, right);
        mix[2*i] = (int32_t)((uint32_t)mix[2*i] + (uint32_t)l);
        mix[2*i+1] = (int32_t)((uint32_t)mix[2*i+1] + (uint32_t)r);
        pos += step;
    }
    *position = pos;
    return 1;
}

int wog_audio_rdft(float *x, double *re, double *im, const int *rev, const double *cosine, const double *sine, int n)
{
    if (!x || !re || !im || !rev || !cosine || !sine || n < 2 || n > 16384 || (n & (n - 1))) return 0;
    for (int i=0;i<n;i++) if (rev[i]<0 || rev[i]>=n) return 0;
    for (int i=0;i<n/2;i++) { re[i]=x[2*i]; im[i]=x[2*i+1]; }
    for (int i=n/2;i<n;i++) re[i]=im[i]=0;
    for (int i=0;i<n;i++) { int j=rev[i]; if(j>i){double t=re[i];re[i]=re[j];re[j]=t;t=im[i];im[i]=im[j];im[j]=t;} }
    for (int size=2;size<=n;size<<=1) {
        int half=size/2,step=n/size;
        for(int start=0;start<n;start+=size) for(int k=0;k<half;k++) {
            int a=start+k,b=a+half;double wr=cosine[k*step],wi=-sine[k*step];
            volatile double p=re[b]*wr,q=im[b]*wi,r=re[b]*wi,s=im[b]*wr;
            double tr=p-q,ti=r+s;
            re[b]=re[a]-tr; im[b]=im[a]-ti; re[a]+=tr; im[a]+=ti;
        }
    }
    double dc=x[0]*0.5,nyq=x[1]*0.5;
    for(int i=0;i<n;i++) x[i]=(float)(re[i]-dc+((i&1)?-nyq:nyq));
    return 1;
}
