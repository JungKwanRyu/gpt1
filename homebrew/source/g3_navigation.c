#include "g3_navigation.h"
#include "g3_menu.h"
#include "ui_font.h"
#include <stdlib.h>
#include <string.h>

typedef struct { int model,index,x,y; } G3BattleSprite;
#include "g3_battle6_data.h"

static int routes(G3Navigation *n) {
    uint8_t occupied[G3_ROUTE_MAX]={0};
    if(n->map.width*n->map.height>G3_ROUTE_MAX)return 0;
    for(unsigned i=0;i<22;i++) {
        const G3BattleUnit *u=&n->units[i];
        if(n->combat_mode && n->combat.hp[i]<=0)continue;
        if(u->x<0 || u->y<0 || (unsigned)u->x>=n->map.width || (unsigned)u->y>=n->map.height)return 0;
        occupied[(unsigned)u->y*n->map.width+(unsigned)u->x]=1;
    }
    G3BattleUnit *u=&n->units[n->selected];
    int budget=n->remaining[n->selected];
    if(n->combat_mode && (n->combat.status || !n->combat.move[n->selected]))budget=0;
    return g3_map_routes(&n->map,u->x,u->y,0,budget,occupied,
                         (size_t)n->map.width*n->map.height,n->cost,n->predecessor);
}
int g3_navigation_reset(G3Navigation *n) {
    if(!n || !n->map_data)return 0;
    memcpy(n->units,initial_units,sizeof(n->units));
    for(unsigned i=0;i<22;i++)n->remaining[i]=n->units[i].movement;
    n->selected=0;n->cursor_x=n->units[0].x;n->cursor_y=n->units[0].y;
    if(!g3_combat_init(&n->combat,initial_combat,22,1))return 0;
    n->active_clock=n->combat.clock;
    if(n->combat_mode) {
        n->selected=n->combat.active;
        n->cursor_x=n->units[n->selected].x;n->cursor_y=n->units[n->selected].y;
    }
    return routes(n);
}
void g3_navigation_close(G3Navigation *n) {
    if(!n)return;
    for(unsigned i=0;i<10;i++)free(n->images[i]);
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
    if(!g3_pack_open(&n->pack,path) || n->pack.count!=G3_BATTLE_COMBAT_HELP+1 || n->pack.count>10)goto fail;
    G3Asset a;
    if(!g3_pack_record(&n->pack,0,&a) || strcmp(a.name,G3_BATTLE_ID))goto fail;
    for(unsigned i=0;i<n->pack.count;i++) {
        if(!g3_pack_record(&n->pack,i,&a) || a.width>640 || a.height>480)goto fail;
        n->images[i]=malloc(a.size);
        if(!n->images[i] || !g3_pack_pixels(&n->pack,&a,n->images[i],a.size))goto fail;
    }
    n->combat_mode=1;
    if(!g3_navigation_reset(n))goto fail;
    return 1;
fail:
    g3_navigation_close(n);return 0;
}
static int sync_combat(G3Navigation *n) {
    for(unsigned i=0;i<22;i++)n->units[i].hp=n->combat.hp[i];
    if(n->combat.active>=0 && (n->selected!=n->combat.active || n->active_clock!=n->combat.clock)) {
        n->selected=n->combat.active;
        n->active_clock=n->combat.clock;
        n->cursor_x=n->units[n->selected].x;n->cursor_y=n->units[n->selected].y;
        n->remaining[n->selected]=n->units[n->selected].movement;
    }
    return routes(n);
}
int g3_navigation_attackable(const G3Navigation *n,unsigned target) {
    if(!n || !n->combat_mode || n->combat.status || n->combat.active<0 || target>=22 ||
       n->combat.hp[target]<=0 || !n->combat.attack[n->combat.active])return 0;
    const G3BattleUnit *a=&n->units[n->combat.active],*b=&n->units[target];
    if(a->team==b->team)return 0;
    int dx=a->x-b->x,dy=a->y-b->y;
    if(dx<0)dx=-dx;
    if(dy<0)dy=-dy;
    unsigned from=(unsigned)a->y*n->map.width+(unsigned)a->x,to=(unsigned)b->y*n->map.width+(unsigned)b->x;
    int dh=(int)n->map.terrain[from]-(int)n->map.terrain[to];
    if(dh<0)dh=-dh;
    const G3CombatStats *s=&n->combat.stats[n->combat.active];
    int distance=4*(dx+dy)+(s->height_cost?2*dh:0);
    return distance>=s->min_range && distance<=s->range;
}
int g3_navigation_input(G3Navigation *n,unsigned buttons) {
    if(!n || !n->map_data || n->selected<0 || n->selected>=22 || (!n->combat_mode && n->selected>=4) ||
       n->cursor_x<0 || n->cursor_y<0 || (unsigned)n->cursor_x>=n->map.width || (unsigned)n->cursor_y>=n->map.height)return 0;
    if(buttons&G3_NAV_TOGGLE){n->combat_mode=!n->combat_mode;return g3_navigation_reset(n);}
    if(buttons&G3_NAV_RESET)return g3_navigation_reset(n);
    if(n->combat_mode) {
        if(buttons&G3_NAV_PREVIOUS) {n->cursor_x=n->units[n->selected].x;n->cursor_y=n->units[n->selected].y;}
        if((buttons&G3_NAV_NEXT) && !n->combat.status) {
            if(!g3_combat_pass(&n->combat))return 0;
            return sync_combat(n);
        }
    }
    if(!n->combat_mode && (buttons&(G3_NAV_PREVIOUS|G3_NAV_NEXT))) {
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
        if(n->combat_mode && !n->combat.status)for(unsigned i=0;i<22;i++) {
            if(n->units[i].x==n->cursor_x && n->units[i].y==n->cursor_y && g3_navigation_attackable(n,i)) {
                if(!g3_combat_strike(&n->combat,i))return 0;
                return sync_combat(n);
            }
        }
        int cost=n->cost[cell];
        if(cost>0 && cost<=n->remaining[n->selected] && (!n->combat_mode || !n->combat.status)) {
            n->units[n->selected].x=n->cursor_x;n->units[n->selected].y=n->cursor_y;
            n->remaining[n->selected]-=cost;
            if(n->combat_mode) {
                n->remaining[n->selected]=0;
                if(!g3_combat_moved(&n->combat))return 0;
                return sync_combat(n);
            }
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
static void caption(uint8_t *canvas,int y,const char *text) {
    for(int x=8;*text && x<624;text++,x+=8) {
        unsigned c=(unsigned char)*text;
        if(c<32 || c>126)continue;
        for(unsigned row=0;row<14;row++)for(unsigned bit=0;bit<8;bit++)
            if(ui_font[c-32][row]&(1u<<bit))pixel(canvas,x+(int)bit,y+(int)row,240,240,220);
    }
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
        if(n->combat_mode && n->combat.hp[order[i]]<=0)continue;
        if(!g3_map_project(&n->map,u->x,u->y,&sx,&sy))return 0;
        marker(canvas,sx-cx,sy-cy,u->team==1?80:240,u->team==1?240:70,80);
        for(unsigned j=0;j<sizeof(battle_sprites)/sizeof(battle_sprites[0]);j++)if(battle_sprites[j].model==u->model) {
            const G3BattleSprite *s=&battle_sprites[j];
            if(!blit(n,(unsigned)s->index,canvas,sx-cx+s->x,sy-cy+s->y))return 0;
        }
        if(n->combat_mode) {
            int hp=n->combat.hp[order[i]],max=n->combat.stats[order[i]].max_hp;
            for(int x=0;x<30;x++)for(int y=0;y<3;y++)pixel(canvas,sx-cx-15+x,sy-cy+13+y,
                x*max<hp*30?60:90,x*max<hp*30?220:40,50);
            if(g3_navigation_attackable(n,order[i]))marker(canvas,sx-cx,sy-cy,255,30,30);
        }
    }
    int sx,sy;
    if(!g3_map_project(&n->map,n->cursor_x,n->cursor_y,&sx,&sy))return 0;
    marker(canvas,sx-cx,sy-cy,255,240,50);
    if(!blit(n,n->combat_mode?G3_BATTLE_COMBAT_HELP:G3_BATTLE_HELP,canvas,0,0))return 0;
    if(n->combat_mode) {
        char line[96];const G3Combat *c=&n->combat;int who=n->selected;
        for(int y=446;y<480;y++)for(int x=0;x<640;x++)pixel(canvas,x,y,8,10,20);
        snprintf(line,sizeof(line),"TIME %u TEAM %d UNIT %d HP %d/%d MOVE %d ATK %d",
                 c->clock,(int)c->stats[who].team,who+1,(int)c->hp[who],(int)c->stats[who].max_hp,
                 (int)c->move[who],(int)c->attack[who]);
        caption(canvas,448,line);
        if(c->status)caption(canvas,464,"MANUAL TEST ENDED - X RESET (NOT ORIGINAL VICTORY)");
        else if(c->last_source>=0) {
            snprintf(line,sizeof(line),"U%d -> U%d DAMAGE %d HITS %d CRIT %d RESULT %d",
                     c->last_source+1,c->last_target+1,c->last_damage,c->last_hits,c->last_critical,c->last_state);
            caption(canvas,464,line);
        } else caption(canvas,464,"BOTH TEAMS MANUAL - SELECT A RED TARGET AND PRESS A");
    }
    return 1;
}
