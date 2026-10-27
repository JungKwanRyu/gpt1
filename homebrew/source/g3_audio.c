#include "g3_audio.h"
#include <string.h>
#include <limits.h>
static uint32_t le32(const uint8_t *p){return (uint32_t)p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static unsigned le16(const uint8_t *p){return (unsigned)p[0]|(unsigned)p[1]<<8;}
void g3_pcm_close(G3Pcm *p){if(p){if(p->file)fclose(p->file);memset(p,0,sizeof(*p));}}
int g3_pcm_open(G3Pcm *p,const char *path,int loop) {
    if(!p || !path)return 0;
    memset(p,0,sizeof(*p));p->file=fopen(path,"rb");if(!p->file)return 0;
    uint8_t header[16];
    if(fseek(p->file,0,SEEK_END))goto fail;
    long length=ftell(p->file);
    if(length<12 || fseek(p->file,0,SEEK_SET) || fread(header,1,12,p->file)!=12 ||
       memcmp(header,"RIFF",4) || memcmp(header+8,"WAVE",4) || (uint64_t)le32(header+4)+8!=(uint64_t)length)goto fail;
    int format=0;uint64_t at=12;
    while(at+8<=(uint64_t)length) {
        if(fseek(p->file,(long)at,SEEK_SET) || fread(header,1,8,p->file)!=8)goto fail;
        uint32_t bytes=le32(header+4);uint64_t next=at+8+bytes+(bytes&1);
        if(next>(uint64_t)length)goto fail;
        if(!memcmp(header,"fmt ",4)) {
            if(format || bytes<16 || fread(header,1,16,p->file)!=16 || le16(header)!=1 ||
               le16(header+2)!=2 || le32(header+4)!=48000 || le32(header+8)!=192000 ||
               le16(header+12)!=4 || le16(header+14)!=16)goto fail;
            format=1;
        } else if(!memcmp(header,"data",4)) {
            if(p->bytes || !bytes || bytes%4 || at+8>UINT32_MAX)goto fail;
            p->offset=(uint32_t)(at+8);p->bytes=bytes;
        }
        at=next;
    }
    if(!format || !p->bytes || at!=(uint64_t)length || fseek(p->file,(long)p->offset,SEEK_SET))goto fail;
    p->loop=!!loop;return 1;
fail:g3_pcm_close(p);return 0;
}
size_t g3_pcm_read(G3Pcm *p,int16_t *samples,size_t frames) {
    if(!p || !p->file || !samples || frames>SIZE_MAX/4)return 0;
    size_t written=0;
    while(written<frames) {
        size_t n=(p->bytes-p->at)/4;
        if(!n) {
            if(!p->loop || fseek(p->file,(long)p->offset,SEEK_SET))break;
            p->at=0;continue;
        }
        if(n>frames-written)n=frames-written;
        size_t got=fread(samples+written*2,4,n,p->file);
        p->at+=(uint32_t)(got*4);written+=got;
        if(got!=n)break;
    }
    return written;
}

#ifdef __SWITCH__
#include <switch.h>
#include <stdlib.h>
#include <malloc.h>
#define AUDIO_FRAMES 4096u
static G3Pcm music;
static AudioOutBuffer buffers[3];
static int current=-1,initialized=0;
void g3_audio_shutdown(void) {
    /* Close the output session before freeing shared sample memory. */
    if(initialized){audoutStopAudioOut();audoutExit();}
    initialized=0;
    for(unsigned i=0;i<3;i++){free(buffers[i].buffer);memset(&buffers[i],0,sizeof(buffers[i]));}
    g3_pcm_close(&music);current=-1;
}
static int queue(AudioOutBuffer *b) {
    size_t frames=g3_pcm_read(&music,b->buffer,AUDIO_FRAMES);
    if(frames!=AUDIO_FRAMES)return 0;
    b->data_size=frames*4;
    armDCacheFlush(b->buffer,b->buffer_size);
    return R_SUCCEEDED(audoutAppendAudioOutBuffer(b));
}
int g3_audio_set_track(int track) {
    if(track==current)return initialized;
    g3_audio_shutdown();
    if(track<0)return 1;
    char path[128];snprintf(path,sizeof(path),"sdmc:/switch/g3nx/audio/bgm%04d.wav",track);
    if(!g3_pcm_open(&music,path,1))return 0;
    if(R_FAILED(audoutInitialize())){g3_pcm_close(&music);return 0;}
    initialized=1;
    if(audoutGetSampleRate()!=48000 || audoutGetChannelCount()!=2 || audoutGetPcmFormat()!=PcmFormat_Int16)goto fail;
    for(unsigned i=0;i<3;i++) {
        buffers[i].buffer=memalign(0x1000,AUDIO_FRAMES*4);
        buffers[i].buffer_size=AUDIO_FRAMES*4;
        if(!buffers[i].buffer || !queue(&buffers[i]))goto fail;
    }
    if(R_FAILED(audoutStartAudioOut()))goto fail;
    current=track;return 1;
fail:g3_audio_shutdown();return 0;
}
void g3_audio_pump(void) {
    if(!initialized)return;
    AudioOutBuffer *released=NULL;u32 count=0;
    for(unsigned i=0;i<3;i++) {
        if(R_FAILED(audoutGetReleasedAudioOutBuffer(&released,&count)) || !count)break;
        if(!queue(released)){g3_audio_shutdown();return;}
    }
}
#endif
