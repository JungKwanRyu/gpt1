#ifndef G3_AUDIO_H
#define G3_AUDIO_H
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
typedef struct { FILE *file; uint32_t offset, bytes, at; int loop; } G3Pcm;
int g3_pcm_open(G3Pcm *pcm,const char *path,int loop);
size_t g3_pcm_read(G3Pcm *pcm,int16_t *samples,size_t frames);
void g3_pcm_close(G3Pcm *pcm);
#ifdef __SWITCH__
/* Called by the frame loop; output failure does not stop the scenario. */
int g3_audio_set_track(int track);
void g3_audio_pump(void);
void g3_audio_shutdown(void);
#endif
#endif
