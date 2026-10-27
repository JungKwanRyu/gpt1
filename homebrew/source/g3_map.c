#include "g3_map.h"
#include <string.h>
#include <limits.h>

typedef struct { const uint8_t *data; size_t size, at; } Reader;
static const uint8_t *take(Reader *r, size_t count) {
    if (count>r->size-r->at) return NULL;
    const uint8_t *p=r->data+r->at;
    r->at+=count;
    return p;
}
static uint32_t u16(const uint8_t *p) { return (uint32_t)p[0]|(uint32_t)p[1]<<8; }
static uint32_t u32(const uint8_t *p) { return u16(p)|u16(p+2)<<16; }
static int signed16(const uint8_t *p) {
    uint32_t value=u16(p);
    return value<32768?(int)value:(int)value-65536;
}
static int dimensions(uint32_t w,uint32_t h) {
    return w>0 && h>0 && w<=4096 && h<=4096 && w*h<=4000000;
}
int g3_map_open(G3Map *map,const uint8_t *data,size_t size) {
    G3Map candidate={0};
    Reader r={data,size,0};
    uint32_t objects, count;
    const uint8_t *p, *object_start, *object_end;
    if (!map) return 0;
    memset(map,0,sizeof(*map));
    if (!data || !(p=take(&r,8)) || u32(p)!=1) return 0;
    candidate.width=u16(p+4); candidate.height=u16(p+6);
    if (!dimensions(candidate.width,candidate.height)) return 0;
    count=candidate.width*candidate.height;
    if (!(candidate.terrain=take(&r,count)) || !(candidate.attributes=take(&r,count))) return 0;
    if (!(p=take(&r,8))) return 0;
    candidate.columns=u16(p); candidate.rows=u16(p+2);
    candidate.origin_x=signed16(p+4); candidate.origin_y=signed16(p+6);
    if (!dimensions(candidate.columns,candidate.rows)) return 0;
    if (!(candidate.cells=take(&r,(size_t)candidate.columns*candidate.rows*4))) return 0;
    if (!(p=take(&r,4)) || (objects=u32(p))>4096) return 0;
    object_start=r.data+r.at;
    for (uint32_t i=0;i<objects;i++) {
        if (!(p=take(&r,8))) return 0;
        uint32_t w=u16(p+4), h=u16(p+6);
        if (!dimensions(w,h) || !take(&r,(size_t)w*h*4)) return 0;
    }
    object_end=r.data+r.at;
    if (!(p=take(&r,4))) return 0;
    candidate.tile_count=u32(p);
    if (!candidate.tile_count || candidate.tile_count>65536) return 0;
    if (!(candidate.tiles=take(&r,(size_t)candidate.tile_count*640)) ||
        !take(&r,candidate.tile_count)) return 0;
    /* This version intentionally accepts only the verified 64-byte map tail. */
    if (r.size-r.at!=64) return 0;
    for (size_t i=0;i<(size_t)candidate.columns*candidate.rows;i++)
        if (u16(candidate.cells+i*4+2)>=candidate.tile_count) return 0;
    p=object_start;
    while (p<object_end) {
        size_t cells=(size_t)u16(p+4)*u16(p+6);
        p+=8;
        for (size_t i=0;i<cells;i++) if (u16(p+i*4+2)>=candidate.tile_count) return 0;
        p+=cells*4;
    }
    *map=candidate;
    return 1;
}
int g3_map_render(const G3Map *map,int x,int y,uint8_t *rgba,size_t capacity,
                  unsigned width,unsigned height) {
    if (!map || !map->tiles || !map->cells || !rgba || !width || !height ||
        width>4096 || height>4096 || capacity<(size_t)width*height*4 ||
        x< -4096 || y< -4096 || x>INT_MAX-4096 || y>INT_MAX-4096) return 0;
    for (unsigned row=0;row<height;row++) for (unsigned col=0;col<width;col++) {
        int sx=x+(int)col, sy=y+(int)row;
        uint8_t *out=rgba+((size_t)row*width+col)*4;
        uint32_t color=0;
        if (sx>=0 && sy>=0 && (unsigned)sx<map->columns*40 && (unsigned)sy<map->rows*8) {
            size_t cell=(size_t)((unsigned)sy/8)*map->columns+(unsigned)sx/40;
            uint32_t tile=u16(map->cells+cell*4+2);
            if (tile>=map->tile_count) return 0;
            size_t pixel=((size_t)tile*320+((unsigned)sy%8)*40+(unsigned)sx%40)*2;
            color=u16(map->tiles+pixel);
        }
        out[0]=(uint8_t)(((color>>10)&31)*255/31);
        out[1]=(uint8_t)(((color>>5)&31)*255/31);
        out[2]=(uint8_t)((color&31)*255/31);
        out[3]=255;
    }
    return 1;
}

static int logical_cell(const G3Map *m,int x,int y) {
    return m && m->terrain && m->attributes && x>=0 && y>=0 &&
        (unsigned)x<m->width && (unsigned)y<m->height;
}
int g3_map_project(const G3Map *m,int x,int y,int *sx,int *sy) {
    if (!sx || !sy || !logical_cell(m,x,y)) return 0;
    *sx=x*40+20-m->origin_x*40;
    *sy=y*32+16-m->terrain[(size_t)y*m->width+x]*12-m->origin_y*8;
    return 1;
}
int g3_map_step(const G3Map *m,int x,int y,int tx,int ty,int flying) {
    if (!logical_cell(m,x,y) || !logical_cell(m,tx,ty)) return -1;
    int dx=tx-x,dy=ty-y;
    if (dx< -1 || dx>1 || dy< -1 || dy>1 || (!dx && !dy)) return -1;
    unsigned from=(unsigned)y*m->width+(unsigned)x,to=(unsigned)ty*m->width+(unsigned)tx;
    if (m->attributes[to] & (flying?2:1)) return -1;
    int dh=(int)m->terrain[to]-(int)m->terrain[from];
    if (dh<0)dh=-dh;
    if (!flying && dh>2) return -1;
    /* Flying altitude uses a different original getter. Do not silently apply
     * ground elevation prices to flying units until that getter is restored. */
    if (flying) return -1;
    return 4*((dx<0?-dx:dx)+(dy<0?-dy:dy))+2*dh;
}
int g3_map_routes(const G3Map *m,int x,int y,int flying,int budget,
                  const uint8_t *occupied,size_t count,int32_t *cost,int32_t *prev) {
    if (!logical_cell(m,x,y) || flying || budget<0 || budget>65535 || !cost || !prev ||
        (size_t)m->width*m->height>G3_ROUTE_MAX || count!=(size_t)m->width*m->height) return 0;
    uint8_t visited[G3_ROUTE_MAX]={0};
    unsigned source=(unsigned)y*m->width+(unsigned)x;
    for (size_t i=0;i<count;i++){cost[i]=-1;prev[i]=-1;}
    cost[source]=0;
    for (;;) {
        int best=-1;
        for (unsigned i=0;i<count;i++)
            if (!visited[i] && cost[i]>=0 && (best<0 || cost[i]<cost[best])) best=(int)i;
        if (best<0) break;
        visited[best]=1;
        int bx=best%(int)m->width,by=best/(int)m->width;
        for (int dy=-1;dy<=1;dy++) for (int dx=-1;dx<=1;dx++) {
            int tx=bx+dx,ty=by+dy,step=g3_map_step(m,bx,by,tx,ty,0);
            if(step<0)continue;
            unsigned to=(unsigned)ty*m->width+(unsigned)tx;
            if (occupied && occupied[to] && to!=source)continue;
            int next=cost[best]+step;
            if (next<=budget && (cost[to]<0 || next<cost[to])) {cost[to]=next;prev[to]=best;}
        }
    }
    return 1;
}
