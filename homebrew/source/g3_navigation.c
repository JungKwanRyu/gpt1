#include "g3_navigation.h"
#include "g3_menu.h"
#include <stdlib.h>
#include <string.h>

typedef struct { int model,index,x,y; } G3BattleSprite;
#include "g3_battle6_data.h"

static int routes(G3Navigation *n) {
    uint8_t occupied[G3_ROUTE_MAX]={0};
    if(n->map.width*n->map.height>G3_ROUTE_MAX)return 0;
    for(unsigned i=0;i<22;i++) {
        const G3BattleUnit *u=&n->units[i];
        if(u->x<0 || u->y<0 || (unsigned)u->x>=n->map.width || (unsigned)u->y>=n->map.height)return 0;
        occupied[(unsigned)u->y*n->map.width+(unsigned)u->x]=1;
    }
    G3BattleUnit *u=&n->units[n->selected];
    return g3_map_routes(&n->map,u->x,u->y,0,n->remaining[n->selected],occupied,
                         (size_t)n->map.width*n->map.height,n->cost,n->predecessor);
}
int g3_navigation_reset(G3Navigation *n) {
    if(!n || !n->map_data)return 0;
    memcpy(n->units,initial_units,sizeof(n->units));
    for(unsigned i=0;i<22;i++)n->remaining[i]=n->units[i].movement;
    n->selected=0;n->cursor_x=n->units[0].x;n->cursor_y=n->units[0].y;
    return routes(n);
}
void g3_navigation_close(G3Navigation *n) {
    if(!n)return;
    for(unsigned i=0;i<8;i++)free(n->images[i]);
    free(n->map_data);g3_pack_close(&n->pack);memset(n,0,sizeof(*n));
}
int g3_navigation_open(G3Navigation *n,const char *folder) {
    if(!n || !folder)return 0;
    memset(n,0,sizeof(*n));
    char path[1024];
    if(snprintf(path,sizeof(path),"%s/battle6.obt",folder)>=(int)sizeof(path))return 0;
    FILE *f=fopen(path,"rb");
    if(!f)return 0;
    n->map_data=malloc(G3_BATTLE_MAP_BYTES);
    int ok=n->map_data && fread(n->map_data,1,G3_BATTLE_MAP_BYTES,f)==G3_BATTLE_MAP_BYTES && fgetc(f)==EOF && !ferror(f);
    fclose(f);
    if(!ok)goto fail;
    uint32_t crc=~0u;
    for(size_t i=0;i<G3_BATTLE_MAP_BYTES;i++) {
        crc^=n->map_data[i];
        for(unsigned bit=0;bit<8;bit++)crc=(crc>>1)^((0u-(crc&1u))&0xedb88320u);
    }
    if((~crc)!=G3_BATTLE_MAP_CRC || !g3_map_open(&n->map,n->map_data,G3_BATTLE_MAP_BYTES))goto fail;
    if(snprintf(path,sizeof(path),"%s/g3nx.battle6",folder)>=(int)sizeof(path))goto fail;
    if(!g3_pack_open(&n->pack,path) || n->pack.count!=G3_BATTLE_HELP+1 || n->pack.count>8)goto fail;
    G3Asset a;
    if(!g3_pack_record(&n->pack,0,&a) || strcmp(a.name,G3_BATTLE_ID))goto fail;
    for(unsigned i=0;i<n->pack.count;i++) {
        if(!g3_pack_record(&n->pack,i,&a) || a.width>640 || a.height>480)goto fail;
        n->images[i]=malloc(a.size);
        if(!n->images[i] || !g3_pack_pixels(&n->pack,&a,n->images[i],a.size))goto fail;
    }
    if(!g3_navigation_reset(n))goto fail;
    return 1;
fail:
    g3_navigation_close(n);return 0;
}
int g3_navigation_input(G3Navigation *n,unsigned buttons) {
    if(!n || !n->map_data || n->selected<0 || n->selected>=4)return 0;
    if(buttons&G3_NAV_RESET)return g3_navigation_reset(n);
    if(buttons&(G3_NAV_PREVIOUS|G3_NAV_NEXT)) {
        n->selected=(n->selected+((buttons&G3_NAV_NEXT)?1:3))%4;
        n->cursor_x=n->units[n->selected].x;n->cursor_y=n->units[n->selected].y;
        return routes(n);
    }
    if((buttons&G3_LEFT) && n->cursor_x>0)n->cursor_x--;
    if((buttons&G3_RIGHT) && (unsigned)n->cursor_x+1<n->map.width)n->cursor_x++;
    if((buttons&G3_UP) && n->cursor_y>0)n->cursor_y--;
    if((buttons&G3_DOWN) && (unsigned)n->cursor_y+1<n->map.height)n->cursor_y++;
    if(buttons&G3_ACCEPT) {
        unsigned cell=(unsigned)n->cursor_y*n->map.width+(unsigned)n->cursor_x;
        int cost=n->cost[cell];
        if(cost>0 && cost<=n->remaining[n->selected]) {
            n->units[n->selected].x=n->cursor_x;n->units[n->selected].y=n->cursor_y;
            n->remaining[n->selected]-=cost;
            return routes(n);
        }
    }
    return 1;
}
static void pixel(uint8_t *canvas,int x,int y,int r,int g,int b) {
    if(x<0 || x>=640 || y<68 || y>=480)return;
    uint8_t *p=canvas+((size_t)y*640+(unsigned)x)*4;
    p[0]=(uint8_t)r;p[1]=(uint8_t)g;p[2]=(uint8_t)b;p[3]=255;
}
static void marker(uint8_t *canvas,int x,int y,int r,int g,int b) {
    for(int i=-16;i<=16;i++) {
        pixel(canvas,x+i,y-10,r,g,b);pixel(canvas,x+i,y+10,r,g,b);
    }
    for(int i=-10;i<=10;i++) {
        pixel(canvas,x-16,y+i,r,g,b);pixel(canvas,x+16,y+i,r,g,b);
    }
}
static int blit(G3Navigation *n,unsigned index,uint8_t *canvas,int dx,int dy) {
    G3Asset a;
    if(!g3_pack_record(&n->pack,index,&a))return 0;
    for(unsigned y=0;y<a.height;y++)for(unsigned x=0;x<a.width;x++) {
        int tx=dx+(int)x,ty=dy+(int)y;
        if(tx<0 || tx>=640 || ty<0 || ty>=480)continue;
        const uint8_t *p=n->images[index]+((size_t)y*a.width+x)*4;
        uint8_t *q=canvas+((size_t)ty*640+(unsigned)tx)*4;
        for(unsigned c=0;c<3;c++)q[c]=(uint8_t)((p[c]*p[3]+q[c]*(255-p[3])+127)/255);
        q[3]=255;
    }
    return 1;
}
int g3_navigation_render(G3Navigation *n,uint8_t *canvas,size_t capacity) {
    if(!n || !n->map_data || !canvas || capacity<640u*480u*4u)return 0;
    int cx,cy;
    if(!g3_map_project(&n->map,n->cursor_x,n->cursor_y,&cx,&cy))return 0;
    cx-=320;cy-=270;
    if(cx<0)cx=0;
    if(cy<0)cy=0;
    if(cx>(int)n->map.columns*40-640)cx=(int)n->map.columns*40-640;
    if(cy>(int)n->map.rows*8-480)cy=(int)n->map.rows*8-480;
    if(!g3_map_render(&n->map,cx,cy,canvas,capacity,640,480))return 0;
    for(unsigned y=0;y<n->map.height;y++)for(unsigned x=0;x<n->map.width;x++) {
        unsigned at=y*n->map.width+x;
        if(n->cost[at]<0)continue;
        int sx,sy;
        if(!g3_map_project(&n->map,(int)x,(int)y,&sx,&sy))return 0;
        marker(canvas,sx-cx,sy-cy,50,160,255);
    }
    unsigned order[22];
    for(unsigned i=0;i<22;i++)order[i]=i;
    for(unsigned i=1;i<22;i++)for(unsigned j=i;j>0 && n->units[order[j]].y<n->units[order[j-1]].y;j--) {
        unsigned tmp=order[j];order[j]=order[j-1];order[j-1]=tmp;
    }
    for(unsigned i=0;i<22;i++) {
        G3BattleUnit *u=&n->units[order[i]];int sx,sy;
        if(!g3_map_project(&n->map,u->x,u->y,&sx,&sy))return 0;
        marker(canvas,sx-cx,sy-cy,u->team==1?80:240,u->team==1?240:70,80);
        for(unsigned j=0;j<sizeof(battle_sprites)/sizeof(battle_sprites[0]);j++)if(battle_sprites[j].model==u->model) {
            const G3BattleSprite *s=&battle_sprites[j];
            if(!blit(n,(unsigned)s->index,canvas,sx-cx+s->x,sy-cy+s->y))return 0;
        }
    }
    int sx,sy;
    if(!g3_map_project(&n->map,n->cursor_x,n->cursor_y,&sx,&sy))return 0;
    marker(canvas,sx-cx,sy-cy,255,240,50);
    return blit(n,G3_BATTLE_HELP,canvas,0,0);
}
