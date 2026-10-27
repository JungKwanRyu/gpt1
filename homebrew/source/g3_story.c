#include "g3_story.h"
#include <stdio.h>
#include <string.h>
#include <limits.h>

typedef struct { uint16_t word[7]; } G3Command;
typedef struct {
    uint16_t kind, condition_count, action_count;
    const G3Command *conditions, *actions;
} G3Event;
typedef struct { int id, asset; } G3ResourceMap;
typedef struct { unsigned first, count; } G3TextPages;
#ifndef G3_STORY_DATA_HEADER
#define G3_STORY_DATA_HEADER "g3_story_data.h"
#endif
#include G3_STORY_DATA_HEADER

uint32_t g3_story_identity(void) { return G3_STORY_ID; }
unsigned g3_story_asset_count(void) { return G3_STORY_ASSETS; }
int g3_story_asset(int kind, int id, int page) {
    unsigned i;
    if (kind==4) return id==1?G3_STORY_BOUNDARY_ASSET:G3_STORY_ERROR_ASSET;
    if (kind==1) {
        for (i=0;i<sizeof(g3_story_backgrounds)/sizeof(*g3_story_backgrounds);i++)
            if (g3_story_backgrounds[i].id==id) return g3_story_backgrounds[i].asset;
    } else if (kind==2) {
        for (i=0;i<sizeof(g3_story_portraits)/sizeof(*g3_story_portraits);i++)
            if (g3_story_portraits[i].id==id) return g3_story_portraits[i].asset;
    } else if (kind==3 && id>=0 && (unsigned)id<sizeof(g3_story_texts)/sizeof(*g3_story_texts) &&
               page>=0 && (unsigned)page<g3_story_texts[id].count) {
        return (int)g3_story_texts[id].first+page;
    }
    return -1;
}
void g3_story_init(G3Story *s) {
    memset(s,0,sizeof(*s));
    s->active=0; s->background=G3_STORY_BACKGROUND;
    s->left=s->right=s->center=s->dialog=s->track=-1;
    s->enabled[0]=1;
}
static int compare(int a,int b,unsigned mode) {
    switch (mode) {
        case 0:return a==b; case 1:return a!=b; case 2:return a<b;
        case 3:return a<=b; case 4:return a>b; case 5:return a>=b;
        default:return 0;
    }
}
static int ready(G3Story *s, const G3Event *event) {
    unsigned i;
    for (i=0;i<event->condition_count;i++) {
        const uint16_t *c=event->conditions[i].word;
        if (c[0]==0) continue; /* No other synchronous task is running here. */
        if (c[0]!=100 || c[1]>=256 || c[3]>5) {
            s->status=G3_STORY_ERROR; s->error=c[0]; return 0;
        }
        if (!compare(s->flags[c[1]],c[2],c[3])) return 0;
    }
    return 1;
}
void g3_story_tick(G3Story *s, int accept) {
    unsigned budget;
    if (s->status!=G3_STORY_RUNNING) return;
    if (s->timer>0) { s->timer--; return; }
    if (s->wait) {
        if (!accept) return;
        if ((unsigned)(s->page+1)<g3_story_texts[s->dialog].count) { s->page++; return; }
        s->wait=0; s->dialog=-1; s->page=0;
    }
    for (budget=0;budget<256;budget++) {
        unsigned i;
        if (s->active<0) {
            for (i=0;i<G3_STORY_EVENTS;i++) {
                if (s->enabled[i] && !s->finished[i] && ready(s,&g3_story_events[i])) break;
                if (s->status) return;
            }
            if (i==G3_STORY_EVENTS) { s->status=G3_STORY_BOUNDARY; return; }
            s->active=(int)i; s->pc=0;
        }
        const G3Event *event=&g3_story_events[s->active];
        if ((unsigned)s->pc>=event->action_count) {
            s->finished[s->active]=1; s->active=-1; continue;
        }
        const uint16_t *c=event->actions[s->pc++].word;
        switch (c[0]) {
            case 0:
                if (c[1]>=G3_STORY_EVENTS) goto unsupported;
                s->enabled[c[1]]=1; break;
            case 1: break; /* Synchronous command barrier. */
            case 2:
                s->timer=c[1]>600?600:c[1]; return;
            case 100:
                if (g3_story_asset(1,c[1],0)<0) goto unsupported;
                s->background=c[1]; break;
            case 101: s->background=-1; break;
            case 103: break; /* Palette fade presentation is not yet reproduced. */
            case 200: case 201: case 202:
                if (g3_story_asset(2,c[1],0)<0 || g3_story_asset(3,c[2],0)<0) goto unsupported;
                if (c[0]==200) s->left=c[1];
                else if (c[0]==201) s->right=c[1];
                else s->center=c[1];
                s->dialog=c[2]; s->page=0; s->wait=1; return;
            case 203: s->left=-1; break;
            case 204: s->right=-1; break;
            case 205: s->center=-1; break;
            case 700: s->track=c[1]; break; /* Track retained; audio backend is pending. */
            case 703: s->next_field=c[1]; break;
            case 800:
                if (c[1]>=256) goto unsupported;
                s->flags[c[1]]=(uint8_t)c[2]; break;
            case 500: s->status=G3_STORY_BOUNDARY; return;
            default:
unsupported:
                s->status=G3_STORY_ERROR; s->error=c[0]; s->pc--; return;
        }
    }
    s->status=G3_STORY_ERROR; s->error=65535;
}

void g3_story_blit(uint8_t *canvas, const uint8_t *layer, int dx) {
    int x,y;
    for (y=0;y<480;y++) for (x=0;x<640;x++) {
        int target=x+dx;
        unsigned channel;
        if (target<0 || target>=640) continue;
        const uint8_t *p=layer+((size_t)y*640+(unsigned)x)*4;
        uint8_t *q=canvas+((size_t)y*640+(unsigned)target)*4;
        unsigned alpha=p[3];
        for (channel=0;channel<3;channel++)
            q[channel]=(uint8_t)((p[channel]*alpha+q[channel]*(255-alpha)+127)/255);
        q[3]=255;
    }
}

#define STATE_SIZE 344u
#define HEADER_SIZE 24u
static void put32(uint8_t *p,uint32_t v) {
    p[0]=(uint8_t)v; p[1]=(uint8_t)(v>>8); p[2]=(uint8_t)(v>>16); p[3]=(uint8_t)(v>>24);
}
static uint32_t get32(const uint8_t *p) {
    return (uint32_t)p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;
}
static uint32_t checksum(const uint8_t *p,unsigned size) {
    uint32_t crc=0xffffffffu;
    unsigned i,j;
    for (i=0;i<size;i++) {
        crc^=p[i];
        for (j=0;j<8;j++) crc=(crc>>1)^(0xedb88320u & (0u-(crc&1u)));
    }
    return ~crc;
}
int g3_story_valid(const G3Story *s) {
    unsigned i;
    if (s->active< -1 || s->active>=(int)G3_STORY_EVENTS || s->pc<0 ||
        (s->active>=0 && s->pc>g3_story_events[s->active].action_count) ||
        s->status<0 || s->status>2 || s->wait<0 || s->wait>1 || s->timer<0 || s->timer>600 ||
        s->page<0 || s->next_field<0 || s->next_field>65535 || s->error<0 || s->error>65535 ||
        s->track< -1 || s->track>65535) return 0;
    if (s->background!= -1 && g3_story_asset(1,s->background,0)<0) return 0;
    if (s->left!= -1 && g3_story_asset(2,s->left,0)<0) return 0;
    if (s->right!= -1 && g3_story_asset(2,s->right,0)<0) return 0;
    if (s->center!= -1 && g3_story_asset(2,s->center,0)<0) return 0;
    if (s->dialog!= -1 && g3_story_asset(3,s->dialog,s->page)<0) return 0;
    if (s->wait && s->dialog<0) return 0;
    for (i=0;i<16;i++) if (s->enabled[i]>1 || s->finished[i]>1) return 0;
    return 1;
}
static int read_slot(G3Story *s,const char *directory,unsigned slot,uint32_t *sequence) {
    char path[512];
    uint8_t bytes[HEADER_SIZE+STATE_SIZE], crc_data[20+STATE_SIZE];
    size_t at=HEADER_SIZE;
    G3Story candidate;
    if (snprintf(path,sizeof(path),"%s/g3nx-progress-%u.bin",directory,slot)>=(int)sizeof(path)) return 0;
    FILE *file=fopen(path,"rb");
    if (!file) return 0;
    int ok=fread(bytes,1,sizeof(bytes),file)==sizeof(bytes) && fgetc(file)==EOF;
    fclose(file);
    if (!ok || memcmp(bytes,"G3SAVE03",8) || get32(bytes+8)!=G3_STORY_ID || get32(bytes+16)!=STATE_SIZE) return 0;
    memcpy(crc_data,bytes,20); memcpy(crc_data+20,bytes+24,STATE_SIZE);
    if (checksum(crc_data,sizeof(crc_data))!=get32(bytes+20)) return 0;
#define G3_DECODE(name) candidate.name=(int32_t)get32(bytes+at); at+=4;
    G3_STORY_FIELDS(G3_DECODE)
#undef G3_DECODE
    memcpy(candidate.flags,bytes+at,256); at+=256;
    memcpy(candidate.enabled,bytes+at,16); at+=16;
    memcpy(candidate.finished,bytes+at,16);
    if (!g3_story_valid(&candidate)) return 0;
    *sequence=get32(bytes+12); *s=candidate;
    return 1;
}
int g3_story_load(G3Story *s,const char *directory) {
    G3Story a,b;
    uint32_t sa=0,sb=0;
    int va=read_slot(&a,directory,0,&sa), vb=read_slot(&b,directory,1,&sb);
    if (!va && !vb) return 0;
    *s=va && (!vb || sa>=sb)?a:b;
    return 1;
}
int g3_story_save(const G3Story *s,const char *directory) {
    G3Story ignored;
    uint32_t a=0,b=0,sequence;
    uint8_t bytes[HEADER_SIZE+STATE_SIZE], crc_data[20+STATE_SIZE];
    size_t at=HEADER_SIZE;
    char path[512];
    if (!g3_story_valid(s)) return 0;
    read_slot(&ignored,directory,0,&a); read_slot(&ignored,directory,1,&b);
    sequence=(a>b?a:b)+1;
    if (!sequence) return 0;
    memcpy(bytes,"G3SAVE03",8); put32(bytes+8,G3_STORY_ID);
    put32(bytes+12,sequence); put32(bytes+16,STATE_SIZE);
#define G3_ENCODE(name) put32(bytes+at,(uint32_t)s->name); at+=4;
    G3_STORY_FIELDS(G3_ENCODE)
#undef G3_ENCODE
    memcpy(bytes+at,s->flags,256); at+=256;
    memcpy(bytes+at,s->enabled,16); at+=16;
    memcpy(bytes+at,s->finished,16);
    memcpy(crc_data,bytes,20); memcpy(crc_data+20,bytes+24,STATE_SIZE);
    put32(bytes+20,checksum(crc_data,sizeof(crc_data)));
    if (snprintf(path,sizeof(path),"%s/g3nx-progress-%u.bin",directory,sequence&1)>=(int)sizeof(path)) return 0;
    FILE *file=fopen(path,"wb");
    if (!file) return 0;
    int ok=fwrite(bytes,1,sizeof(bytes),file)==sizeof(bytes) && fflush(file)==0;
    if (fclose(file)!=0) ok=0;
    return ok;
}
