#include "g3_session.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#ifdef _WIN32
#include <windows.h>
#else
#include <unistd.h>
#endif
#define PAYLOAD (4u+G3_PROGRESS_BYTES+344u+4u*G3_NAV_SAVE_WORDS)
#define HEADER 24u
static void put(uint8_t *p,uint32_t v){for(unsigned i=0;i<4;i++)p[i]=(uint8_t)(v>>(8*i));}
static uint32_t get(const uint8_t *p){return (uint32_t)p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static uint32_t crc(const uint8_t *p,size_t n){uint32_t v=~0u;while(n--){v^=*p++;for(unsigned i=0;i<8;i++)v=(v>>1)^(0xedb88320u&(0u-(v&1u)));}return ~v;}
static uint32_t identity(void){return g3_progress_identity()^g3_aftermath_identity()^g3_navigation_identity();}
static uint32_t checksum(const uint8_t *b){uint8_t check[20+PAYLOAD];memcpy(check,b,20);memcpy(check+20,b+HEADER,PAYLOAD);return crc(check,sizeof(check));}
static int path_for(char *path,size_t capacity,const char *directory,unsigned slot){int n=snprintf(path,capacity,"%s/g3nx-session-%u.bin",directory,slot);return n>=0 && (size_t)n<capacity;}
static int decode(const uint8_t *b,int *stage,G3Progress *p,G3Navigation *n,G3Story *a) {
    uint32_t st=get(b);if(st>2 || !g3_progress_decode(p,b+4))return 0;
    g3_story_decode_state(a,b+4+G3_PROGRESS_BYTES);if(!g3_aftermath_valid(a))return 0;
    uint32_t words[G3_NAV_SAVE_WORDS];
    for(unsigned i=0;i<G3_NAV_SAVE_WORDS;i++)words[i]=get(b+4+G3_PROGRESS_BYTES+344+4*i);
    if(st==1 && !g3_navigation_import(n,words,G3_NAV_SAVE_WORDS))return 0;
    if(st!=1)for(unsigned i=0;i<G3_NAV_SAVE_WORDS;i++)if(words[i])return 0;
    *stage=(int)st;return 1;
}
static int read_slot(uint8_t *b,const char *directory,unsigned slot,uint32_t *sequence,int *exists) {
    char path[1024];if(!path_for(path,sizeof(path),directory,slot))return 0;
    FILE *f=fopen(path,"rb");if(!f)return 0;*exists=1;
    int ok=fread(b,1,HEADER+PAYLOAD,f)==HEADER+PAYLOAD && fgetc(f)==EOF && !ferror(f);fclose(f);
    if(!ok || memcmp(b,"G3SAVE07",8) || get(b+8)!=identity() || !get(b+12) || get(b+16)!=PAYLOAD || get(b+20)!=checksum(b))return 0;
    *sequence=get(b+12);return 1;
}
int g3_session_load(int *stage,G3Progress *p,G3Navigation *n,G3Story *a,const char *directory) {
    if(!stage || !p || !n || !a || !directory)return 0;
    uint8_t b[2][HEADER+PAYLOAD];uint32_t seq[2]={0};int exists=0;
    int valid[2]={read_slot(b[0],directory,0,&seq[0],&exists),read_slot(b[1],directory,1,&seq[1],&exists)};
    unsigned first=seq[1]>seq[0]?1:0;
    for(unsigned i=0;i<2;i++) {
        unsigned slot=first^i;if(!valid[slot])continue;
        G3Progress candidate;G3Story after;int st;
        if(decode(b[slot]+HEADER,&st,&candidate,n,&after)){*stage=st;*p=candidate;*a=after;return 1;}
    }
    if(exists)return 0;
    G3Progress legacy;if(!g3_progress_load(&legacy,directory))return 0;
    *p=legacy;*stage=0;g3_aftermath_init(a);return 1;
}
int g3_session_save(int stage,const G3Progress *p,const G3Navigation *n,const G3Story *a,const char *directory) {
    if(stage<0 || stage>2 || !p || !a || !directory || !g3_progress_valid(p) || !g3_aftermath_valid(a))return 0;
    uint32_t words[G3_NAV_SAVE_WORDS]={0};
    if(stage==1 && !g3_navigation_export(n,words,G3_NAV_SAVE_WORDS))return 0;
    uint8_t b[HEADER+PAYLOAD],old[HEADER+PAYLOAD];uint32_t x=0,y=0;int exists=0;
    read_slot(old,directory,0,&x,&exists);read_slot(old,directory,1,&y,&exists);
    uint32_t seq=(x>y?x:y)+1;if(!seq)return 0;
    memset(b,0,sizeof(b));memcpy(b,"G3SAVE07",8);put(b+8,identity());put(b+12,seq);put(b+16,PAYLOAD);
    put(b+HEADER,(uint32_t)stage);g3_progress_encode(b+HEADER+4,p);g3_story_encode_state(b+HEADER+4+G3_PROGRESS_BYTES,a);
    for(unsigned i=0;i<G3_NAV_SAVE_WORDS;i++)put(b+HEADER+4+G3_PROGRESS_BYTES+344+4*i,words[i]);
    put(b+20,checksum(b));char path[1024],tmp[1030];
    if(!path_for(path,sizeof(path),directory,seq&1))return 0;
    int len=snprintf(tmp,sizeof(tmp),"%s.tmp",path);if(len<0 || (size_t)len>=sizeof(tmp))return 0;
    FILE *f=fopen(tmp,"wb");if(!f)return 0;
    int ok=fwrite(b,1,sizeof(b),f)==sizeof(b) && fflush(f)==0;
    if(fclose(f))ok=0;
    if(ok) {
#ifdef _WIN32
        if(MoveFileExA(tmp,path,MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH))return 1;
#else
        if(rename(tmp,path)==0)return 1;
#endif
    }
    remove(tmp);return 0;
}
