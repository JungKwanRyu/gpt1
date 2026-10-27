#ifndef G3_COMBAT_H
#define G3_COMBAT_H
#include <stdint.h>
#define G3_COMBAT_MAX 22u
enum { G3_DODGE=1,G3_BLOCK=2,G3_PERFECT_BLOCK=4,G3_UNBLOCKABLE=8 };
/* Initial, unmounted, small ground units; physical effect kind 0 only.
 * Battle statuses, counterattacks, abilities and original AI/events are absent. */
typedef struct {
    int32_t max_hp,strength,dexterity,armor,power,modifier,accuracy,critical;
    int32_t block_accuracy,block_defense,flags,period,team,attack_id,hits,bonus;
    int32_t range,min_range,height_cost,element,secondary,scales[9];
} G3CombatStats;
typedef struct {
    G3CombatStats stats[G3_COMBAT_MAX];
    int32_t hp[G3_COMBAT_MAX],move[G3_COMBAT_MAX],attack[G3_COMBAT_MAX];
    uint32_t count,clock,phase,rng;
    int32_t active,last_source,last_target,last_damage,last_hits,last_critical,last_state,status;
} G3Combat;
/* Morale-dependent original stat getter, before battle statuses. */
int32_t g3_combat_morale_stat(int32_t stat,int32_t morale);
int g3_combat_init(G3Combat *combat,const G3CombatStats *stats,unsigned count,uint32_t seed);
int g3_combat_next(G3Combat *combat);
int g3_combat_pass(G3Combat *combat);
int g3_combat_moved(G3Combat *combat);
/* Range/position legality must be checked by the map controller first.
 * Unsupported data or illegal action returns 0 without changing state/RNG. */
int g3_combat_strike(G3Combat *combat,unsigned target);
#endif
