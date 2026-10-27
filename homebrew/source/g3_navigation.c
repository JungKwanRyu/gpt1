#include "g3_navigation.h"
#include "g3_menu.h"
#include "ui_font.h"
#include "g3_animation.h"
#include "g3_combat_math.h"
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
    g3_battle_events_init(&n->events);
    n->motion=0;n->source=n->target=-1;n->time=n->duration=n->path_count=n->path_index=n->step_time=0;
    n->origin_x=n->origin_y=0;
    n->ai_enabled=1;n->ai_delay=0;memset(n->effects,0,sizeof(n->effects));
    memset(n->path,0,sizeof(n->path));memset(n->clock,0,sizeof(n->clock));
    for(unsigned i=0;i<22;i++){n->direction[i]=initial_direction[i];n->action[i]=initial_direction[i]==3?1:initial_direction[i];}
    n->active_clock=n->combat.clock;
    if(n->combat_mode) {
        n->selected=n->combat.active;
        n->cursor_x=n->units[n->selected].x;n->cursor_y=n->units[n->selected].y;
    }
    return routes(n);
}
void g3_navigation_close(G3Navigation *n) {
    if(!n)return;
    if(n->images)for(unsigned i=0;i<n->pack.count;i++)free(n->images[i]);
    free(n->images);
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
    if(!g3_pack_open(&n->pack,path) || n->pack.count!=G3_BATTLE_ASSETS || n->pack.count>2048)goto fail;
    G3Asset a;
    if(!g3_pack_record(&n->pack,0,&a) || strcmp(a.name,G3_BATTLE_ID))goto fail;
    n->images=calloc(n->pack.count,sizeof(*n->images));
    if(!n->images)goto fail;
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
static const G3BattleModel *unit_model(const G3Navigation *n,unsigned unit) {
    for(unsigned i=0;i<sizeof(battle_models)/sizeof(battle_models[0]);i++)
        if(battle_models[i].model==n->units[unit].model)return &battle_models[i];
    return NULL;
}
static const G3BattleModel *effect_model(int id) {
    for(unsigned i=0;i<sizeof(battle_models)/sizeof(battle_models[0]);i++)if(battle_models[i].model==id)return &battle_models[i];
    return NULL;
}
static int effect_add(G3Navigation *n,int model,int action,int anchor,int dx,int dy,int dz,int mirror) {
    const G3BattleModel *m=effect_model(model);
    if(!m || action<0 || (unsigned)action>=m->actions)return 0;
    for(unsigned i=0;i<16;i++)if(!n->effects[i].model) {
        G3BattleEffect e={model,action,0,anchor,dx,dy,dz,mirror};n->effects[i]=e;return 1;
    }
    return 0;
}
static int short_signed(unsigned v){return v<32768?(int)v:(int)v-65536;}
static int facing(int dx,int dy) {return abs(dx)>=abs(dy)?(dx<0?1:3):(dy<0?0:2);}
static int distance_from(const G3Navigation *n,int x,int y,unsigned target) {
    const G3BattleUnit *b=&n->units[target];
    int dh=(int)n->map.terrain[(unsigned)y*n->map.width+(unsigned)x]-
        (int)n->map.terrain[(unsigned)b->y*n->map.width+(unsigned)b->x];
    return 4*(abs(x-b->x)+abs(y-b->y))+2*abs(dh);
}
static int destination(const G3Navigation *n,unsigned source,int x,int y) {
    if(n->units[source].x==x && n->units[source].y==y)return 1;
    int height=n->map.terrain[(unsigned)y*n->map.width+(unsigned)x];
    for(unsigned i=0;i<22;i++)if(i!=source && n->combat.hp[i]>0) {
        const G3BattleUnit *u=&n->units[i];
        if(u->x==x && u->y==y)return 0;
        /* 1008A020/10089C60: cannot stop immediately beside a hostile at
         * the same elevation. This is separate from path terrain costs. */
        if(g3_combat_hostile(n->units[source].team,u->team) && abs(u->x-x)+abs(u->y-y)==1 &&
           n->map.terrain[(unsigned)u->y*n->map.width+(unsigned)u->x]==height)return 0;
    }
    return 1;
}
int g3_navigation_attackable(const G3Navigation *n,unsigned target) {
    if(!n || !n->combat_mode || n->combat.status || n->combat.active<0 || target>=22 ||
       n->combat.hp[target]<=0 || !n->combat.attack[n->combat.active])return 0;
    const G3BattleUnit *a=&n->units[n->combat.active],*b=&n->units[target];
    if(!g3_combat_hostile(a->team,b->team))return 0;
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
        int consumed=n->events.wait || !n->events.ready;
        if(n->motion!=2 && !g3_battle_events_tick(n,!!(buttons&G3_ACCEPT)))return 0;
        if(consumed || n->events.wait || n->events.outcome>=0 || n->motion)return 1;
        if(n->ai_enabled && n->combat.active>=0 && n->combat.stats[n->combat.active].team!=4)return 1;
    }
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
                n->motion=2;n->source=n->selected;n->target=(int)i;n->time=0;
                int dx=n->units[i].x-n->units[n->source].x,dy=n->units[i].y-n->units[n->source].y;
                n->direction[n->source]=(abs(dx)>=abs(dy))?(dx<0?1:3):(dy<0?0:2);
                int base=6;
                if(n->combat.stats[n->source].attack_id==1 && (g3_original_rand(&n->combat.rng)&1))base=9;
                n->action[n->source]=base+(n->direction[n->source]==3?1:n->direction[n->source]);
                const G3BattleModel *m=unit_model(n,(unsigned)n->source);
                n->duration=(int)m->action[n->action[n->source]].duration;
                n->clock[n->source]=0;n->combat.attack[n->source]=0;
                n->combat.last_source=n->source;n->combat.last_target=(int)i;
                n->combat.last_damage=n->combat.last_hits=n->combat.last_critical=0;
                return 1;
            }
        }
        int cost=n->cost[cell];
        if(cost>0 && cost<=n->remaining[n->selected] && (!n->combat_mode ||
           (!n->combat.status && destination(n,(unsigned)n->selected,n->cursor_x,n->cursor_y)))) {
            if(n->combat_mode) {
                int reversed[64],count=0,at=(int)cell;
                int origin=n->units[n->selected].y*(int)n->map.width+n->units[n->selected].x;
                while(at!=origin && count<64){reversed[count++]=at;at=n->predecessor[at];if(at<0)return 0;}
                if(at!=origin)return 0;
                n->path_count=count;n->path_index=n->step_time=0;
                n->origin_x=n->units[n->selected].x;n->origin_y=n->units[n->selected].y;
                n->duration=count*6;
                for(int i=0;i<count;i++)n->path[i]=reversed[count-i-1];
                n->motion=1;n->source=n->selected;n->target=-1;n->time=0;
                return 1;
            }
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
static int estimate(G3Navigation *n,unsigned source,unsigned target) {
    const G3CombatStats *a=&n->combat.stats[source],*d=&n->combat.stats[target];
    int strength=g3_combat_morale_stat(a->strength,80),armor=g3_combat_morale_stat(d->armor,80);
    int64_t value=(int64_t)(a->power+10)*strength+a->modifier*a->modifier+a->bonus*a->bonus;
    if(a->element)value=value*d->scales[a->element-1]/10;
    if(a->secondary)value=value*d->scales[a->secondary+4]/10;
    if(armor)value/=armor;
    if(value>n->combat.hp[target])value=n->combat.hp[target];
    int damage=g3_original_damage_variation((int)value,&n->combat.rng);
    /* 1002B180 -> 1003FEC0 -> 10041020, profile 1's kind-0 branch.
     * Forecasting itself consumes the original RNG. No critical roll here. */
    int resistance=g3_combat_morale_stat(initial_resistance[target],80);
    return (int)(((int64_t)damage*a->hits*(armor+resistance)*20/200)/100);
}
static unsigned targets(G3Navigation *n,unsigned who,int radius,unsigned *out) {
    int score[22];unsigned count=0;
    for(unsigned i=0;i<22;i++)if(n->combat.hp[i]>0 && g3_combat_hostile(n->units[who].team,n->units[i].team) &&
        distance_from(n,n->units[who].x,n->units[who].y,i)<=radius) {
        out[count]=i;score[count++]=estimate(n,who,i);
    }
    /* Preserve the target-pointer swaps of 1002B284, including its fixed
     * score-array comparisons rather than silently replacing its sort. */
    for(unsigned end=count;end>1;end--)for(unsigned j=0;j+1<end;j++)if(score[j]<score[j+1]) {
        unsigned v=out[j];out[j]=out[j+1];out[j+1]=v;
    }
    return count>10?10:count;
}
static int ai_accept(G3Navigation *n,int x,int y) {
    n->cursor_x=x;n->cursor_y=y;
    n->ai_enabled=0;int ok=g3_navigation_input(n,G3_ACCEPT);n->ai_enabled=1;
    return ok;
}
static int ai_step(G3Navigation *n) {
    unsigned who=(unsigned)n->combat.active;
    if(initial_ai[who]!=1)return 0;
    if(!routes(n))return 0;
    unsigned cells=n->map.width*n->map.height;
    int retreat=n->combat.hp[who]<=n->combat.stats[who].max_hp*2/10;
    if(n->combat.move[who]) {
        int best=-1;
        if(retreat) {
            int far=-1;
            int current=1000;
            for(unsigned j=0;j<22;j++)if(n->combat.hp[j]>0 && g3_combat_hostile(n->units[who].team,n->units[j].team)) {
                int dh=abs((int)n->map.terrain[(unsigned)n->units[who].y*n->map.width+(unsigned)n->units[who].x]-
                           (int)n->map.terrain[(unsigned)n->units[j].y*n->map.width+(unsigned)n->units[j].x]);
                int d=abs(n->units[who].x-n->units[j].x)+abs(n->units[who].y-n->units[j].y)+dh/2;
                if(d<current)current=d;
            }
            if(current>10) {
                if(!g3_combat_pass(&n->combat))return 0;
                return sync_combat(n);
            }
            for(unsigned i=0;i<cells;i++)if(n->cost[i]>=0) {
                int x=(int)(i%n->map.width),y=(int)(i/n->map.width),near=1000;
                if(!destination(n,who,x,y))continue;
                for(unsigned j=0;j<22;j++)if(n->combat.hp[j]>0 && g3_combat_hostile(n->units[who].team,n->units[j].team)) {
                    int dh=abs((int)n->map.terrain[(unsigned)y*n->map.width+(unsigned)x]-
                               (int)n->map.terrain[(unsigned)n->units[j].y*n->map.width+(unsigned)n->units[j].x]);
                    int d=abs(x-n->units[j].x)+abs(y-n->units[j].y)+dh/2;if(d<near)near=d;
                }
                if(near>far){far=near;best=(int)i;}
            }
        } else {
            unsigned list[22];int radius=n->units[who].movement+n->combat.stats[who].range;
            if(radius>100)radius=100;
            unsigned count=targets(n,who,radius,list);
            for(unsigned j=0;j<count && best<0;j++) {
                int cheapest=65536;
                for(unsigned i=0;i<cells;i++)if(n->cost[i]>=0 && n->cost[i]<cheapest) {
                    int x=(int)(i%n->map.width),y=(int)(i/n->map.width);
                    int d=distance_from(n,x,y,list[j]);
                    if(!destination(n,who,x,y) || d<n->combat.stats[who].min_range || d>n->combat.stats[who].range)continue;
                    cheapest=n->cost[i];best=(int)i;
                }
            }
            if(best<0) {
                count=targets(n,who,100,list);
                int32_t cost[G3_ROUTE_MAX],previous[G3_ROUTE_MAX];
                for(unsigned j=0;j<count && best<0;j++) {
                    const G3BattleUnit *target=&n->units[list[j]];
                    if(!g3_map_routes(&n->map,target->x,target->y,0,65535,NULL,cells,cost,previous))return 0;
                    int nearest=65536;
                    for(unsigned i=0;i<cells;i++)if(n->cost[i]>=0 && cost[i]>=0 && cost[i]<nearest) {
                        int x=(int)(i%n->map.width),y=(int)(i/n->map.width);
                        if(!destination(n,who,x,y))continue;
                        nearest=cost[i];best=(int)i;
                    }
                }
            }
        }
        if(best>=0 && n->cost[best]>0)return ai_accept(n,best%(int)n->map.width,best/(int)n->map.width);
    }
    if(!retreat && n->combat.attack[who]) {
        int best=-1,value=-1;
        for(unsigned i=0;i<22;i++)if(g3_navigation_attackable(n,i)) {
            int score=estimate(n,who,i);
            if(score>=value){value=score;best=(int)i;}
        }
        if(best>=0)return ai_accept(n,n->units[best].x,n->units[best].y);
    }
    if(!g3_combat_pass(&n->combat))return 0;
    return sync_combat(n);
}
int g3_navigation_tick(G3Navigation *n) {
    if(!n || !n->map_data)return 0;
    if(!n->combat_mode)return 1;
    for(unsigned i=0;i<16;i++)if(n->effects[i].model) {
        G3BattleEffect *e=&n->effects[i];const G3BattleModel *m=effect_model(e->model);
        if(!m || e->action<0 || (unsigned)e->action>=m->actions)return 0;
        if(++e->clock>=(int)m->action[e->action].duration)memset(e,0,sizeof(*e));
    }
    for(unsigned i=0;i<22;i++) {
        const G3BattleModel *m=unit_model(n,i);
        if(!m || n->action[i]<0 || (unsigned)n->action[i]>=m->actions)return 0;
        if(n->clock[i]<1000000)n->clock[i]++;
        if(n->action[i]>=15 && (unsigned)n->clock[i]>=m->action[n->action[i]].duration) {
            n->action[i]=n->direction[i]==3?1:n->direction[i];n->clock[i]=0;
        }
    }
    if(n->motion!=2 && !g3_battle_events_tick(n,0))return 0;
    if(n->events.wait || !n->events.ready || n->events.outcome>=0)return 1;
    if(n->motion==1) {
        unsigned who=(unsigned)n->source;
        int cell=n->path[n->path_index],x=cell%(int)n->map.width,y=cell/(int)n->map.width;
        n->direction[who]=facing(x-n->units[who].x,y-n->units[who].y);
        n->action[who]=3+(n->direction[who]==3?1:n->direction[who]);
        if(++n->step_time>=6) {
            n->units[who].x=x;n->units[who].y=y;n->cursor_x=x;n->cursor_y=y;n->step_time=0;
            if(++n->path_index>=n->path_count) {
                n->motion=0;n->remaining[who]=0;n->action[who]=n->direction[who]==3?1:n->direction[who];
                if(!g3_combat_moved(&n->combat) || !sync_combat(n))return 0;
            }
        }
        return g3_battle_events_tick(n,0);
    }
    if(n->motion==2) {
        unsigned who=(unsigned)n->source,target=(unsigned)n->target;
        const G3BattleModel *m=unit_model(n,who);const G3ObsAction *a=&m->action[n->action[who]];
        n->clock[who]=n->time;
        for(unsigned j=0;j<a->count;j++) {
            const uint16_t *c=a->commands[j].word;
            if(c[0]==n->time && c[2]==2) {
                int mirror=c[8] && n->direction[who]==3,dx=short_signed(c[5]);
                if(mirror)dx=-dx;
                if(!effect_add(n,c[3],c[4],(int)who,dx,short_signed(c[6]),short_signed(c[7]),mirror))return 0;
            }
            if(c[0]==n->time && c[2]==5 && n->combat.hp[target]>0) {
                if(!g3_combat_packet(&n->combat,who,target,c[3]))return 0;
                if(n->combat.last_damage>0 && (n->combat.hp[target]==0 || n->combat.last_state==2)) {
                    /* KO uses fixed blood action 0 (1003DECF); surviving hits consume the random choice. */
                    unsigned choice=n->combat.hp[target]==0?0:((n->combat.rng>>16)&32767u)%3;
                    if(!effect_add(n,796,(int)choice,(int)target,0,0,30,0))return 0;
                }
                n->units[target].hp=n->combat.hp[target];
                n->action[target]=15+(n->direction[target]==3?1:n->direction[target]);n->clock[target]=0;
            }
        }
        if(++n->time>= (int)a->duration) {
            n->motion=0;n->action[who]=n->direction[who]==3?1:n->direction[who];n->clock[who]=0;
            if(!n->combat.move[who] || n->combat.hp[target]<=0)if(!g3_combat_next(&n->combat))return 0;
            if(!sync_combat(n))return 0;
        }
        return 1;
    }
    if(n->combat.active>=0 && n->combat.phase!=(unsigned)n->combat.stats[n->combat.active].team) {
        if(!g3_combat_next(&n->combat) || !sync_combat(n))return 0;
    }
    if(n->ai_enabled && !n->combat.status && n->combat.active>=0 && n->combat.stats[n->combat.active].team!=4) {
        if(++n->ai_delay>=12){n->ai_delay=0;return ai_step(n);}
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
static int draw_asset(G3Navigation *n,unsigned index,uint8_t *canvas,int dx,int dy,int mirror) {
    G3Asset a;
    if(!g3_pack_record(&n->pack,index,&a))return 0;
    if(!n->images[index]) {
        n->images[index]=malloc(a.size);
        if(!n->images[index] || !g3_pack_pixels(&n->pack,&a,n->images[index],a.size))return 0;
    }
    for(unsigned y=0;y<a.height;y++)for(unsigned x=0;x<a.width;x++) {
        int tx=dx+(int)x,ty=dy+(int)y;
        if(tx<0 || tx>=640 || ty<0 || ty>=480)continue;
        unsigned sx=mirror?a.width-1-x:x;
        const uint8_t *p=n->images[index]+((size_t)y*a.width+sx)*4;
        uint8_t *q=canvas+((size_t)ty*640+(unsigned)tx)*4;
        for(unsigned c=0;c<3;c++)q[c]=(uint8_t)((p[c]*p[3]+q[c]*(255-p[3])+127)/255);
        q[3]=255;
    }
    return 1;
}
static int blit(G3Navigation *n,unsigned index,uint8_t *canvas,int dx,int dy) {
    return draw_asset(n,index,canvas,dx,dy,0);
}
static G3BattleFrame actor_frame(const G3Navigation *n,unsigned who) {
    const G3BattleModel *m=unit_model(n,who);
    unsigned action=(unsigned)n->action[who];
    if(m && action<m->actions && m->action[action].duration) {
        const G3ObsAction *a=&m->action[action];
        unsigned time=(unsigned)n->clock[who]%a->duration;
        for(unsigned i=0;i<a->count;i++) {
            const uint16_t *c=a->commands[i].word;
            if(c[2]==0 && time>=c[0] && time-c[0]<c[1] && c[4]<m->frames)return m->frame[c[4]];
        }
    }
    for(unsigned i=0;i<sizeof(battle_sprites)/sizeof(battle_sprites[0]);i++)if(battle_sprites[i].model==n->units[who].model) {
        G3BattleFrame f={battle_sprites[i].index,battle_sprites[i].x,battle_sprites[i].y};return f;
    }
    G3BattleFrame missing={-1,0,0};return missing;
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
        if(n->combat_mode && !destination(n,(unsigned)n->selected,(int)x,(int)y))continue;
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
        if(n->motion==1 && order[i]==(unsigned)n->source && n->path_index<n->path_count) {
            int next=n->path[n->path_index],tx,ty;
            if(!g3_map_project(&n->map,next%(int)n->map.width,next/(int)n->map.width,&tx,&ty))return 0;
            sx+=(tx-sx)*n->step_time/6;sy+=(ty-sy)*n->step_time/6;
        }
        marker(canvas,sx-cx,sy-cy,u->team==4?80:240,u->team==4?240:70,80);
        G3BattleFrame sprite=actor_frame(n,order[i]);G3Asset asset;
        if(sprite.asset<0 || !g3_pack_record(&n->pack,(unsigned)sprite.asset,&asset))return 0;
        int mirrored=n->direction[order[i]]==3;
        int px=mirrored?sx-cx-sprite.x-(int)asset.width:sx-cx+sprite.x;
        if(!draw_asset(n,(unsigned)sprite.asset,canvas,px,sy-cy+sprite.y,mirrored))return 0;
        if(n->combat_mode) {
            int hp=n->combat.hp[order[i]],max=n->combat.stats[order[i]].max_hp;
            for(int x=0;x<30;x++)for(int y=0;y<3;y++)pixel(canvas,sx-cx-15+x,sy-cy+13+y,
                x*max<hp*30?60:90,x*max<hp*30?220:40,50);
            if(g3_navigation_attackable(n,order[i]))marker(canvas,sx-cx,sy-cy,255,30,30);
        }
    }
    for(unsigned i=0;i<16;i++)if(n->effects[i].model) {
        const G3BattleEffect *e=&n->effects[i];const G3BattleModel *m=effect_model(e->model);
        const G3ObsAction *a=&m->action[e->action];int sx,sy;
        const G3BattleUnit *u=&n->units[e->anchor];
        if(!g3_map_project(&n->map,u->x,u->y,&sx,&sy))return 0;
        sx+=e->dx;sy+=e->dy*32/40-e->dz*12/20;
        for(unsigned j=0;j<a->count;j++) {
            const uint16_t *c=a->commands[j].word;
            if(c[2]!=0 || (unsigned)e->clock<c[0] || (unsigned)e->clock-c[0]>=c[1])continue;
            if(c[4]>=m->frames)return 0;
            const G3BattleFrame *f=&m->frame[c[4]];G3Asset asset;
            if(!g3_pack_record(&n->pack,(unsigned)f->asset,&asset))return 0;
            int x=e->mirror?sx-cx-f->x-(int)asset.width:sx-cx+f->x;
            if(!draw_asset(n,(unsigned)f->asset,canvas,x,sy-cy+f->y,e->mirror))return 0;
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
        if(c->status)caption(canvas,464,"BATTLE END - FOLLOW THE SCENARIO MESSAGE");
        else if(c->last_source>=0) {
            snprintf(line,sizeof(line),"U%d -> U%d DAMAGE %d HITS %d CRIT %d RESULT %d",
                     c->last_source+1,c->last_target+1,c->last_damage,c->last_hits,c->last_critical,c->last_state);
            caption(canvas,464,line);
        } else caption(canvas,464,n->ai_enabled?"ENEMY AI ENABLED - SELECT A RED TARGET AND PRESS A":"MANUAL RULE TEST");
        if(n->events.wait) {
            if(!blit(n,G3_BATTLE_DIALOG_FIRST+(unsigned)n->events.dialog,canvas,0,326))return 0;
            int character=n->units[n->events.speaker].character;
            for(unsigned i=0;i<sizeof(battle_names)/sizeof(battle_names[0]);i++)if(battle_names[i]==character)
                if(!blit(n,G3_BATTLE_NAME_FIRST+i,canvas,16,330))return 0;
        }
        if(n->events.outcome>=0 && !blit(n,G3_BATTLE_END_FIRST+(n->events.outcome==0?0:1),canvas,0,210))return 0;
    }
    return 1;
}

uint32_t g3_navigation_identity(void){return G3_BATTLE_ID_VALUE;}
int g3_navigation_valid(const G3Navigation *n) {
    if(!n || !n->map_data || !n->images || n->combat.count!=22 ||
       n->selected<0 || n->selected>=22 || n->cursor_x<0 || n->cursor_y<0 ||
       (unsigned)n->cursor_x>=n->map.width || (unsigned)n->cursor_y>=n->map.height ||
       n->combat_mode<0 || n->combat_mode>1 || n->combat.clock<1 || n->combat.clock>1000000 ||
       n->combat.phase>4 || n->combat.status<0 || n->combat.status>1 ||
       n->combat.active< -1 || n->combat.active>=22 || (!n->combat.status && n->combat.active<0) ||
       n->ai_enabled<0 || n->ai_enabled>1 || n->ai_delay<0 || n->ai_delay>12 ||
       n->motion<0 || n->motion>2 || n->source< -1 || n->source>=22 || n->target< -1 || n->target>=22 ||
       n->time<0 || n->time>10000 || n->duration<0 || n->duration>10000 ||
       n->path_count<0 || n->path_count>64 || n->path_index<0 || n->path_index>n->path_count ||
       n->step_time<0 || n->step_time>=6 || !g3_battle_events_valid(&n->events))return 0;
    if(n->combat_mode && n->combat.active>=0 &&
       (n->selected!=n->combat.active || n->active_clock!=n->combat.clock ||
        n->combat.hp[n->combat.active]<=0 || n->combat.stats[n->combat.active].period<=0 || n->combat.clock%(unsigned)n->combat.stats[n->combat.active].period))return 0;
    for(unsigned i=0;i<22;i++) {
        const G3BattleUnit *u=&n->units[i],*v=&initial_units[i];
        G3CombatStats st=n->combat.stats[i];st.team=initial_combat[i].team;
        if(memcmp(&st,&initial_combat[i],sizeof(st)) || u->character!=v->character || u->model!=v->model ||
           u->movement!=v->movement || u->period!=v->period || u->team!=n->combat.stats[i].team ||
           (u->team!=v->team && !(i>=19 && u->team==3)) ||
           u->x<0 || u->y<0 || (unsigned)u->x>=n->map.width || (unsigned)u->y>=n->map.height ||
           u->hp!=n->combat.hp[i] || u->hp<0 || u->hp>st.max_hp ||
           n->remaining[i]<0 || n->remaining[i]>v->movement ||
           n->combat.move[i]<0 || n->combat.move[i]>1 || n->combat.attack[i]<0 || n->combat.attack[i]>1 ||
           n->direction[i]<0 || n->direction[i]>3 || n->action[i]<0 || n->action[i]>17 ||
           n->clock[i]<0 || n->clock[i]>1000000)return 0;
        for(unsigned j=0;j<i;j++)if(u->hp>0 && n->units[j].hp>0 && u->x==n->units[j].x && u->y==n->units[j].y)return 0;
    }
    if(n->combat.last_source< -1 || n->combat.last_source>=22 || n->combat.last_target< -1 ||
       n->combat.last_target>=22 || n->combat.last_damage<0 || n->combat.last_damage>1000000 ||
       n->combat.last_hits<0 || n->combat.last_hits>8 || n->combat.last_critical<0 ||
       n->combat.last_critical>n->combat.last_hits || n->combat.last_state<0 || n->combat.last_state>7)return 0;
    if(n->motion) {
        if(n->source<0 || n->source>=22 || n->source!=n->combat.active || n->time<0 ||
           n->duration<1 || n->duration>10000)return 0;
        if(n->motion==2) {
            const G3BattleModel *m=unit_model(n,(unsigned)n->source);
            if(n->target<0 || n->target>=22 || n->target==n->source ||
               n->action[n->source]<6 || n->action[n->source]>11 ||
               n->duration!=(int)m->action[n->action[n->source]].duration || n->time>=n->duration)return 0;
        } else {
            if(n->path_count<1 || n->path_count>64 || n->path_index<0 || n->path_index>=n->path_count ||
               n->step_time<0 || n->step_time>=6 || n->origin_x<0 || n->origin_y<0 ||
               (unsigned)n->origin_x>=n->map.width || (unsigned)n->origin_y>=n->map.height)return 0;
            int x=n->origin_x,y=n->origin_y,total=0;
            for(int i=0;i<n->path_count;i++) {
                int cell=n->path[i];if(cell<0 || (unsigned)cell>=n->map.width*n->map.height)return 0;
                int nx=cell%(int)n->map.width,ny=cell/(int)n->map.width;
                int cost=g3_map_step(&n->map,x,y,nx,ny,0);
                if(cost<0)return 0;
                total+=cost;x=nx;y=ny;
            }
            if(total>n->units[n->source].movement)return 0;
            int cell=n->path_index?n->path[n->path_index-1]:n->origin_y*(int)n->map.width+n->origin_x;
            if(n->units[n->source].x!=cell%(int)n->map.width || n->units[n->source].y!=cell/(int)n->map.width)return 0;
        }
    }
    for(unsigned i=0;i<16;i++) {
        const G3BattleEffect *e=&n->effects[i];
        if(!e->model) {G3BattleEffect zero={0};if(memcmp(e,&zero,sizeof(zero)))return 0;continue;}
        const G3BattleModel *m=effect_model(e->model);
        if((e->model!=231 && e->model!=291 && e->model!=669 && e->model!=880 && e->model!=796) ||
           !m || e->action<0 || (unsigned)e->action>=m->actions || e->clock<0 ||
           (unsigned)e->clock>=m->action[e->action].duration || e->anchor<0 || e->anchor>=22 ||
           e->dx< -32768 || e->dx>32768 || e->dy< -32768 || e->dy>32767 || e->dz< -32768 || e->dz>32767 ||
           e->mirror<0 || e->mirror>1)return 0;
    }
    return 1;
}
#define NAV_SCALARS(X) X(selected) X(cursor_x) X(cursor_y) X(combat_mode) X(active_clock) X(ai_enabled) X(ai_delay) X(motion) X(source) X(target) X(time) X(duration) X(path_count) X(path_index) X(step_time) X(origin_x) X(origin_y)
#define COMBAT_SCALARS(X) X(count) X(clock) X(phase) X(rng) X(active) X(last_source) X(last_target) X(last_damage) X(last_hits) X(last_critical) X(last_state) X(status)
int g3_navigation_export(const G3Navigation *n,uint32_t *w,unsigned count) {
    if(!w || count!=G3_NAV_SAVE_WORDS || !g3_navigation_valid(n))return 0;
    unsigned k=0;
#define WRITE(f) w[k++]=(uint32_t)n->f;
    NAV_SCALARS(WRITE)
#undef WRITE
    for(unsigned i=0;i<22;i++) {
        const int32_t v[]={n->units[i].x,n->units[i].y,n->units[i].team,n->remaining[i],n->combat.hp[i],n->combat.move[i],n->combat.attack[i],n->direction[i],n->action[i],n->clock[i]};
        for(unsigned j=0;j<10;j++)w[k++]=(uint32_t)v[j];
    }
#define WRITE(f) w[k++]=(uint32_t)n->combat.f;
    COMBAT_SCALARS(WRITE)
#undef WRITE
#define WRITE(f) w[k++]=(uint32_t)n->events.f;
    G3_BATTLE_EVENT_FIELDS(WRITE)
#undef WRITE
    for(unsigned i=0;i<16;i++)w[k++]=n->events.enabled[i];
    for(unsigned i=0;i<16;i++)w[k++]=n->events.finished[i];
    for(unsigned i=0;i<64;i++)w[k++]=(uint32_t)n->path[i];
    for(unsigned i=0;i<16;i++) {
        const G3BattleEffect *e=&n->effects[i];const int32_t v[]={e->model,e->action,e->clock,e->anchor,e->dx,e->dy,e->dz,e->mirror};
        for(unsigned j=0;j<8;j++)w[k++]=(uint32_t)v[j];
    }
    return k==count;
}
static int32_t signed_word(uint32_t v){return v<=INT32_MAX?(int32_t)v:-1-(int32_t)(UINT32_MAX-v);}
int g3_navigation_import(G3Navigation *n,const uint32_t *w,unsigned count) {
    if(!n || !n->map_data || !w || count!=G3_NAV_SAVE_WORDS)return 0;
    G3Navigation *p=malloc(sizeof(*p));if(!p)return 0;*p=*n;unsigned k=0;
#define READ(f) p->f=signed_word(w[k++]);
    NAV_SCALARS(READ)
#undef READ
    for(unsigned i=0;i<22;i++) {
        int32_t *v[]={&p->units[i].x,&p->units[i].y,&p->units[i].team,&p->remaining[i],&p->combat.hp[i],&p->combat.move[i],&p->combat.attack[i],&p->direction[i],&p->action[i],&p->clock[i]};
        for(unsigned j=0;j<10;j++)*v[j]=signed_word(w[k++]);
        p->units[i].hp=p->combat.hp[i];p->combat.stats[i].team=p->units[i].team;
    }
#define READ(f) p->combat.f=signed_word(w[k++]);
    COMBAT_SCALARS(READ)
#undef READ
#define READ(f) p->events.f=signed_word(w[k++]);
    G3_BATTLE_EVENT_FIELDS(READ)
#undef READ
    int ok=1;
    for(unsigned i=0;i<16;i++){uint32_t v=w[k++];if(v>1)ok=0;p->events.enabled[i]=(uint8_t)v;}
    for(unsigned i=0;i<16;i++){uint32_t v=w[k++];if(v>1)ok=0;p->events.finished[i]=(uint8_t)v;}
    for(unsigned i=0;i<64;i++)p->path[i]=signed_word(w[k++]);
    for(unsigned i=0;i<16;i++) {
        G3BattleEffect *e=&p->effects[i];int32_t *v[]={&e->model,&e->action,&e->clock,&e->anchor,&e->dx,&e->dy,&e->dz,&e->mirror};
        for(unsigned j=0;j<8;j++)*v[j]=signed_word(w[k++]);
    }
    ok=ok && k==count && g3_navigation_valid(p) && routes(p);
    if(ok)*n=*p;
    free(p);return ok;
}

unsigned g3_navigation_sounds(const G3Navigation *n,int *sounds,unsigned capacity) {
    if(!n || !sounds || !capacity || n->motion!=2 || n->source<0 || n->source>=22 || n->time<1)return 0;
    const G3BattleModel *m=unit_model(n,(unsigned)n->source);
    const G3ObsAction *a=&m->action[n->action[n->source]];unsigned count=0;
    for(unsigned i=0;i<a->count && count<capacity;i++) {
        const uint16_t *c=a->commands[i].word;
        if(c[2]==1 && c[0]==n->time-1)sounds[count++]=c[3];
    }
    return count;
}
