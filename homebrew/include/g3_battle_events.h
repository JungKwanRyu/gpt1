#ifndef G3_BATTLE_EVENTS_H
#define G3_BATTLE_EVENTS_H
#include <stdint.h>
#define G3_BATTLE_EVENT_FIELDS(X) X(active) X(pc) X(dialog) X(speaker) X(wait) X(outcome) X(ready) X(intro_done) X(match_unit) X(error)
typedef struct {
#define DECLARE(n) int32_t n;
    G3_BATTLE_EVENT_FIELDS(DECLARE)
#undef DECLARE
    uint8_t enabled[16],finished[16];
} G3BattleEvents;
struct G3Navigation;
void g3_battle_events_init(G3BattleEvents *events);
int g3_battle_events_tick(struct G3Navigation *nav,int accept);
int g3_battle_events_valid(const G3BattleEvents *events);
#endif
