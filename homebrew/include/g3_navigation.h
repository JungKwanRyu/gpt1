#ifndef G3_NAVIGATION_H
#define G3_NAVIGATION_H
#include "g3_map.h"
#include "g3_formats.h"
#include "g3_combat.h"

/* Battle 6 manual-combat/navigation workbench; not the original battle lifecycle. */
typedef struct { int32_t character,model,x,y,team,movement,hp,period; } G3BattleUnit;
typedef struct {
    uint8_t *map_data;
    G3Map map;
    G3Pack pack;
    uint8_t *images[10];
    G3BattleUnit units[22];
    int32_t remaining[22],selected,cursor_x,cursor_y;
    int32_t cost[G3_ROUTE_MAX],predecessor[G3_ROUTE_MAX];
    G3Combat combat;
    int32_t combat_mode;
    uint32_t active_clock;
} G3Navigation;
enum { G3_NAV_PREVIOUS=128,G3_NAV_NEXT=256,G3_NAV_RESET=512,G3_NAV_TOGGLE=1024 };
/* Open a new/closed instance. Close before reopening; failures leave it closed. */
int g3_navigation_open(G3Navigation *nav,const char *folder);
void g3_navigation_close(G3Navigation *nav);
int g3_navigation_reset(G3Navigation *nav);
int g3_navigation_input(G3Navigation *nav,unsigned buttons);
int g3_navigation_render(G3Navigation *nav,uint8_t *canvas,size_t capacity);
int g3_navigation_attackable(const G3Navigation *nav,unsigned target);
#endif
