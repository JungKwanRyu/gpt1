#include "g3_progress.h"
#include <stdio.h>
#include <string.h>
#ifdef _WIN32
#include <windows.h>
#endif

/* Fixed little-endian wire encoding: no compiler struct layout on disk. */
#define STORY_BYTES 344u
#define FIELD_BYTES (STORY_BYTES+20u+160u)
#define PAYLOAD (4u+STORY_BYTES+FIELD_BYTES)
#define HEADER 24u
static void put(uint8_t *p,uint32_t x){for(unsigned i=0;i<4;i++)p[i]=(uint8_t)(x>>(i*8));}
static uint32_t get(const uint8_t *p){return (uint32_t)p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static uint32_t crc(const uint8_t *p,size_t n) {
    uint32_t v=~0u;
    while(n--){v^=*p++;for(unsigned i=0;i<8;i++)v=(v>>1)^(0xedb88320u&(0u-(v&1u)));}
    return ~v;
}
static void story_encode(uint8_t *p,const G3Story *s) {
#define ENCODE(n) put(p,(uint32_t)s->n);p+=4;
    G3_STORY_FIELDS(ENCODE)
#undef ENCODE
    memcpy(p,s->flags,256);p+=256;memcpy(p,s->enabled,16);p+=16;memcpy(p,s->finished,16);
}
static void story_decode(G3Story *s,const uint8_t *p) {
#define DECODE(n) s->n=(int32_t)get(p);p+=4;
    G3_STORY_FIELDS(DECODE)
#undef DECODE
    memcpy(s->flags,p,256);p+=256;memcpy(s->enabled,p,16);p+=16;memcpy(s->finished,p,16);
}
static void encode(uint8_t *p,const G3Progress *s) {
    put(p,(uint32_t)s->stage);p+=4;
    story_encode(p,&s->story);p+=STORY_BYTES;story_encode(p,&s->field.view);p+=STORY_BYTES;
    const int32_t camera[]={s->field.camera_x,s->field.camera_y,s->field.camera_dx,s->field.camera_dy,s->field.camera_remaining};
    for(unsigned i=0;i<5;i++){put(p,(uint32_t)camera[i]);p+=4;}
    for(unsigned i=0;i<4;i++) {
#define ENCODE(n) put(p,(uint32_t)s->field.actors[i].n);p+=4;
        G3_FIELD_ACTOR_FIELDS(ENCODE)
#undef ENCODE
    }
}
static void decode(G3Progress *s,const uint8_t *p) {
    memset(s,0,sizeof(*s));s->stage=(int32_t)get(p);p+=4;
    story_decode(&s->story,p);p+=STORY_BYTES;story_decode(&s->field.view,p);p+=STORY_BYTES;
    int32_t *camera[]={&s->field.camera_x,&s->field.camera_y,&s->field.camera_dx,&s->field.camera_dy,&s->field.camera_remaining};
    for(unsigned i=0;i<5;i++){*camera[i]=(int32_t)get(p);p+=4;}
    for(unsigned i=0;i<4;i++) {
#define DECODE(n) s->field.actors[i].n=(int32_t)get(p);p+=4;
        G3_FIELD_ACTOR_FIELDS(DECODE)
#undef DECODE
    }
}
void g3_progress_init(G3Progress *s) {
    memset(s,0,sizeof(*s));g3_story_init(&s->story);g3_field_init(&s->field);
}
static int valid(const G3Progress *s) {
    return s && s->stage>=0 && s->stage<=1 && g3_story_valid(&s->story) && g3_field_valid(&s->field);
}
static uint32_t identity(void){return g3_story_identity()^g3_field_identity();}
static int read_slot(G3Progress *s,const char *directory,unsigned slot,uint32_t *sequence,int *exists) {
    char path[512];uint8_t bytes[HEADER+PAYLOAD],check[20+PAYLOAD];
    if(snprintf(path,sizeof(path),"%s/g3nx-campaign-%u.bin",directory,slot)>=(int)sizeof(path))return 0;
    FILE *file=fopen(path,"rb");if(!file)return 0;
    *exists=1;
    int ok=fread(bytes,1,sizeof(bytes),file)==sizeof(bytes) && fgetc(file)==EOF;fclose(file);
    if(!ok || memcmp(bytes,"G3SAVE04",8) || get(bytes+8)!=identity() || get(bytes+16)!=PAYLOAD || !get(bytes+12))return 0;
    memcpy(check,bytes,20);memcpy(check+20,bytes+24,PAYLOAD);
    if(crc(check,sizeof(check))!=get(bytes+20))return 0;
    G3Progress candidate;decode(&candidate,bytes+HEADER);
    if(!valid(&candidate))return 0;
    *s=candidate;*sequence=get(bytes+12);return 1;
}
int g3_progress_load(G3Progress *s,const char *directory) {
    if(!s || !directory)return 0;
    G3Progress a,b;uint32_t sa=0,sb=0;int exists=0;
    int va=read_slot(&a,directory,0,&sa,&exists),vb=read_slot(&b,directory,1,&sb,&exists);
    if(va || vb){*s=va && (!vb || sa>=sb)?a:b;return 1;}
    if(exists)return 0; /* Never silently revive stale legacy saves after corruption. */
    g3_progress_init(&a);
    if(!g3_story_load(&a.story,directory))return 0;
    *s=a;return 1;
}
int g3_progress_save(const G3Progress *s,const char *directory) {
    if(!directory || !valid(s))return 0;
    G3Progress ignored;uint32_t a=0,b=0;int exists=0;
    read_slot(&ignored,directory,0,&a,&exists);read_slot(&ignored,directory,1,&b,&exists);
    uint32_t sequence=(a>b?a:b)+1;if(!sequence)return 0;
    uint8_t bytes[HEADER+PAYLOAD],check[20+PAYLOAD];
    memcpy(bytes,"G3SAVE04",8);put(bytes+8,identity());put(bytes+12,sequence);put(bytes+16,PAYLOAD);
    encode(bytes+HEADER,s);memcpy(check,bytes,20);memcpy(check+20,bytes+24,PAYLOAD);put(bytes+20,crc(check,sizeof(check)));
    char path[512],temporary[520];
    if(snprintf(path,sizeof(path),"%s/g3nx-campaign-%u.bin",directory,sequence&1)>=(int)sizeof(path) ||
       snprintf(temporary,sizeof(temporary),"%s.tmp",path)>=(int)sizeof(temporary))return 0;
    FILE *file=fopen(temporary,"wb");if(!file)return 0;
    int ok=fwrite(bytes,1,sizeof(bytes),file)==sizeof(bytes) && fflush(file)==0;
    if(fclose(file)!=0)ok=0;
    if(ok) {
#ifdef _WIN32
        if(MoveFileExA(temporary,path,MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH))return 1;
#else
        if(rename(temporary,path)==0)return 1;
#endif
    }
    remove(temporary);return 0;
}

int g3_progress_valid(const G3Progress *p){return valid(p);}
uint32_t g3_progress_identity(void){return identity();}
int g3_progress_encode(uint8_t *out,const G3Progress *p){if(!out || !valid(p))return 0;encode(out,p);return 1;}
int g3_progress_decode(G3Progress *p,const uint8_t *in){if(!p || !in)return 0;G3Progress c;decode(&c,in);if(!valid(&c))return 0;*p=c;return 1;}
void g3_story_encode_state(uint8_t *out,const G3Story *p){story_encode(out,p);}
void g3_story_decode_state(G3Story *p,const uint8_t *in){story_decode(p,in);}
