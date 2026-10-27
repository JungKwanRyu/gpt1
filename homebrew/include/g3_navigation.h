#ifndef G3_NAVIGATION_H
#define G3_NAVIGATION_H
#include "g3_map.h"
#include "g3_formats.h"
#include "g3_combat.h"
#include "g3_battle_events.h"

/* First-battle controller; navigation workbench is an optional debug mode. */
typedef struct { int32_t character,model,x,y,team,movement,hp,period; } G3BattleUnit;
typedef struct { int32_t model,action,clock,anchor,dx,dy,dz,mirror; } G3BattleEffect;
typedef struct G3Navigation {
    uint8_t *map_data;
    G3Map map;
    G3Pack pack;
    uint8_t **images;
    G3BattleUnit units[22];
    int32_t remaining[22],selected,cursor_x,cursor_y;
    int32_t cost[G3_ROUTE_MAX],predecessor[G3_ROUTE_MAX];
    G3Combat combat;
    int32_t combat_mode;
    uint32_t active_clock;
    G3BattleEvents events;
    int32_t direction[22],action[22],clock[22];
    int32_t motion,source,target,time,duration,path_count,path_index,step_time,origin_x,origin_y;
    int32_t path[64],ai_enabled,ai_delay;
    G3BattleEffect effects[16];
} G3Navigation;
enum { G3_NAV_PREVIOUS=128,G3_NAV_NEXT=256,G3_NAV_RESET=512,G3_NAV_TOGGLE=1024 };
/* Open a new/closed instance. Close before reopening; failures leave it closed. */
int g3_navigation_open(G3Navigation *nav,const char *folder);
void g3_navigation_close(G3Navigation *nav);
int g3_navigation_reset(G3Navigation *nav);
int g3_navigation_input(G3Navigation *nav,unsigned buttons);
int g3_navigation_render(G3Navigation *nav,uint8_t *canvas,size_t capacity);
int g3_navigation_attackable(const G3Navigation *nav,unsigned target);
int g3_navigation_tick(G3Navigation *nav);
unsigned g3_navigation_sounds(const G3Navigation *nav,int *sounds,unsigned capacity);
#define G3_NAV_SAVE_WORDS 483u
uint32_t g3_navigation_identity(void);
int g3_navigation_valid(const G3Navigation *nav);
int g3_navigation_export(const G3Navigation *nav,uint32_t *words,unsigned count);
int g3_navigation_import(G3Navigation *nav,const uint32_t *words,unsigned count);
#endif
